// Copyright 2020-present Etherna SA
// This file is part of MongODM.
// 
// MongODM is free software: you can redistribute it and/or modify it under the terms of the
// GNU Lesser General Public License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// 
// MongODM is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY;
// without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public License along with MongODM.
// If not, see <https://www.gnu.org/licenses/>.

using Etherna.MongoDB.Bson;
using Etherna.MongoDB.Bson.IO;
using Etherna.MongoDB.Bson.Serialization;
using Etherna.MongoDB.Bson.Serialization.Conventions;
using Etherna.MongoDB.Bson.Serialization.Serializers;
using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.Extensions;
using Etherna.MongODM.Core.ProxyModels;
using Etherna.MongODM.Core.Serialization.Mapping;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core.Serialization.Serializers
{
    public class ModelMapSerializer<TModel>(IDbContext dbContext) :
        SerializerBase<TModel>,
        IBsonDocumentSerializer,
        IBsonIdProvider,
        IModelMapsHandlingSerializer
    {
        // Fields.
        private IDiscriminatorConvention _discriminatorConvention = default!;

        // Properties.
        public BsonClassMapSerializer<TModel> DefaultBsonClassMapSerializer =>
            (BsonClassMapSerializer<TModel>)dbContext.MapRegistry.GetModelMap(typeof(TModel)).ActiveSchema.BsonClassMap.ToSerializer();

        public IDiscriminatorConvention DiscriminatorConvention =>
            _discriminatorConvention ??= dbContext.DiscriminatorRegistry.LookupDiscriminatorConvention(typeof(TModel));

        public IEnumerable<IModelMap> HandledModelMaps => [dbContext.MapRegistry.GetModelMap(typeof(TModel))];

        // Methods.
        public override TModel Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            ArgumentNullException.ThrowIfNull(context, nameof(context));

            // Check if null.
            if (context.Reader.CurrentBsonType == BsonType.Null)
            {
                context.Reader.ReadNull();
                return default!;
            }

            // Find pre-deserialization information.
            //get actual type and schema
            var actualType = DiscriminatorConvention.GetActualType(context.Reader, args.NominalType);
            var actualTypeModelMap = dbContext.MapRegistry.GetModelMap(actualType);

            //deserialize on document
            var bsonDocument = BsonDocumentSerializer.Instance.Deserialize(context, args);

            //get model map id
            string? modelMapId = null;
            if (bsonDocument.TryGetElement(dbContext.Options.ModelMapVersion.ElementName, out BsonElement modelMapIdElement))
            {
                modelMapId = BsonValueToModelMapId(modelMapIdElement.Value);
                bsonDocument.RemoveElement(modelMapIdElement); //don't report into extra elements
            }

            // Initialize localContext.
            using var bsonReader = new BsonDocumentReader(bsonDocument);
            var localContext = BsonDeserializationContext.CreateRoot(bsonReader, builder =>
            {
                builder.AllowDuplicateElementNames = context.AllowDuplicateElementNames;
                builder.DynamicArraySerializer = context.DynamicArraySerializer;
                builder.DynamicDocumentSerializer = context.DynamicDocumentSerializer;
            });

            // Deserialize.
            TModel model;

            //if a correct model map is identified with its id
            if (modelMapId != null && actualTypeModelMap.SchemasById.TryGetValue(modelMapId, out var modelMapSchema))
            {
                var task = DeserializeModelMapSchemaHelperAsync(modelMapSchema, localContext, args);
                task.Wait();
                model = task.Result;
            }

            //else, if a fallback serializator exists
            else if (actualTypeModelMap.FallbackSerializer != null)
            {
                model = (TModel)actualTypeModelMap.FallbackSerializer.Deserialize(localContext, args);
            }

            //else, if a fallback model map exists
            else if (actualTypeModelMap.FallbackSchema != null)
            {
                var task = DeserializeModelMapSchemaHelperAsync(actualTypeModelMap.FallbackSchema, localContext, args);
                task.Wait();
                model = task.Result;
            }

            //else, deserialize wih current active model map schema
            else
            {
                var task = DeserializeModelMapSchemaHelperAsync(actualTypeModelMap.ActiveSchema, localContext, args);
                task.Wait();
                model = task.Result;
            }

            // Add model to cache (if proxy).
            /* Proxy models enable different features. Anyway, if the model as not been created as a proxy
             * (for example for tests scope) these additional operations are not possible or required.
             * In this case, don't add any not-proxy models in cache.
             */
            if (!dbContext.SerializerModifierAccessor.IsNoCacheEnabled &&
                dbContext.ProxyGenerator.IsProxyType(model!.GetType()) &&
                GetDocumentId(model, out var id, out _, out _) && id != null)
            {
                if (dbContext.DbCache.LoadedModels.ContainsKey(id))
                {
                    var fullModel = model;
                    model = (TModel)dbContext.DbCache.LoadedModels[id];

                    if (((IReferenceable)model!).IsSummary)
                        ((IReferenceable)model).MergeFullModel(fullModel);
                }
                else
                {
                    dbContext.DbCache.AddModel(id, (IEntityModel)model);
                }
            }

            // Enable auditing.
            (model as IAuditable)?.EnableAuditing();

            return model;
        }

        public bool GetDocumentId(object document, out object id, out Type idNominalType, out IIdGenerator idGenerator) =>
            DefaultBsonClassMapSerializer.GetDocumentId(document, out id, out idNominalType, out idGenerator);

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, TModel value)
        {
            ArgumentNullException.ThrowIfNull(context, nameof(context));

            // Serialize null object.
            if (value == null)
            {
                context.Writer.WriteNull();
                return;
            }

            // Clear extra elements.
            if (value is IModel model)
                model.ExtraElements?.Clear();

            // Initialize localContext, bsonDocument and bsonWriter.
            var bsonDocument = new BsonDocument();
            using var bsonWriter = new ExtendedBsonDocumentWriter(bsonDocument)
            {
                IsRootDocument = context.Writer is not ExtendedBsonDocumentWriter
            };
            var localContext = BsonSerializationContext.CreateRoot(
                bsonWriter,
                builder => builder.IsDynamicType = context.IsDynamicType);

            // Get default schema.
            var actualType = value.GetType();
            var modelMap = dbContext.MapRegistry.GetModelMap(actualType);

            // Serialize.
            modelMap.ActiveSchema.BsonClassMap.ToSerializer().Serialize(localContext, args, value);

            // Add additional data.
            //add model map id

            /* Verify if already exists, because if current model type is derived from the basic collection type,
             * the basic type serializer is called before, and a more specific serializer as been already invoked
             * from bson class map serializer. In that case, the right model map id is already be setted, and we
             * don't have to replace it with the one wrong of the basic collection model type.
             */
            if (!bsonDocument.Contains(dbContext.Options.ModelMapVersion.ElementName))
                bsonDocument.InsertAt(0, dbContext.MapRegistry.GetActiveModelMapIdBsonElement(actualType));

            // Serialize document.
            BsonDocumentSerializer.Instance.Serialize(context, args, bsonDocument);
        }

        public void SetDocumentId(object document, object id) =>
            DefaultBsonClassMapSerializer.SetDocumentId(document, id);

        public bool TryGetMemberSerializationInfo(string memberName, out BsonSerializationInfo serializationInfo) =>
            DefaultBsonClassMapSerializer.TryGetMemberSerializationInfo(memberName, out serializationInfo);

        // Helpers.
        private static string? BsonValueToModelMapId(BsonValue bsonValue) =>
            bsonValue switch
            {
                BsonNull _ => null,
                BsonString bsonString => bsonString.AsString,
                _ => throw new NotSupportedException(),
            };

        private static async Task<TModel> DeserializeModelMapSchemaHelperAsync(
            IModelMapSchema modelMapSchema,
            BsonDeserializationContext context,
            BsonDeserializationArgs args)
        {
            var serializer = modelMapSchema.BsonClassMap.ToSerializer();

            // If model map schema ask to override the nominal type, override it on args.
            var modelMapSchemaType = modelMapSchema.GetType();
            if (modelMapSchemaType.IsGenericType &&
                modelMapSchemaType.GetGenericTypeDefinition() == typeof(ModelMapSchema<,>))
                args = new BsonDeserializationArgs { NominalType = modelMapSchema.BsonClassMap.ClassType };

            // Deserialize.
            var model = (TModel)serializer.Deserialize(context, args);

            // Fix model.
            return (TModel)await modelMapSchema.FixDeserializedModelAsync(model).ConfigureAwait(false);
        }
    }
}
