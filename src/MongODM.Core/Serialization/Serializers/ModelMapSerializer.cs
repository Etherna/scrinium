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
using Etherna.MongODM.Core.Utility;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core.Serialization.Serializers
{
    public class ModelMapSerializer<TModel>(IDbContextEngine dbContextEngine) :
        SerializerBase<TModel>,
        IBsonDocumentSerializer,
        IBsonIdProvider,
        IModelMapsHandlingSerializer
    {
        // Consts.
        /// <summary>
        /// Maximum number of distinct unrecognized model map schema ids reported by a model map
        /// serializer. Schema ids come from documents, so the already reported ones can't be
        /// remembered without a bound.
        /// </summary>
        public const int MaxWarnedUnrecognizedSchemaIds = 100;

        // Fields.
        private IDiscriminatorConvention _discriminatorConvention = null!;

        private readonly HashSet<(Type ModelType, string? SchemaId)> warnedUnrecognizedSchemaIds = [];

        // Properties.
        public BsonClassMapSerializer<TModel> DefaultBsonClassMapSerializer =>
            (BsonClassMapSerializer<TModel>)dbContextEngine.MapRegistry.GetModelMap(typeof(TModel)).ActiveSchema.Serializer;

        public IDiscriminatorConvention DiscriminatorConvention =>
            _discriminatorConvention ??= dbContextEngine.DiscriminatorRegistry.LookupDiscriminatorConvention(typeof(TModel));

        public IEnumerable<IModelMap> HandledModelMaps => [dbContextEngine.MapRegistry.GetModelMap(typeof(TModel))];

        // Methods.
        public override TModel Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            ArgumentNullException.ThrowIfNull(context);

            // Check if null.
            if (context.Reader.CurrentBsonType == BsonType.Null)
            {
                context.Reader.ReadNull();
                return default!;
            }

            // Find pre-deserialization information.
            //get actual type and schema
            var actualType = DiscriminatorConvention.GetActualType(context.Reader, args.NominalType);
            var actualTypeModelMap = dbContextEngine.MapRegistry.GetModelMap(actualType);

            //deserialize on document
            var bsonDocument = BsonDocumentSerializer.Instance.Deserialize(context, args);

            //get model map schema id
            var schemaId = ModelMapSchemaIdHelper.ExtractSchemaId(bsonDocument, dbContextEngine.Options.ModelMapSchemaId);

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
            if (schemaId != null && actualTypeModelMap.SchemasById.TryGetValue(schemaId, out var modelMapSchema))
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
                /* The schema id is document content: an id matching no registered schema, with
                 * no fallback declared for it, means the document was written by something this
                 * db context doesn't know, and the active schema is reading a shape it was never
                 * meant to read. Report it once per model type and id, so the degradation
                 * doesn't stay silent, up to the reported ids bound. */
                bool firstOccurrence;
                lock (warnedUnrecognizedSchemaIds)
                    firstOccurrence = warnedUnrecognizedSchemaIds.Count < MaxWarnedUnrecognizedSchemaIds &&
                                      warnedUnrecognizedSchemaIds.Add((actualType, schemaId));
                if (firstOccurrence)
                    dbContextEngine.Logger.ModelMapSerializerUnrecognizedSchemaId(
                        dbContextEngine.Options.DbName, actualType.Name, schemaId);

                var task = DeserializeModelMapSchemaHelperAsync(actualTypeModelMap.ActiveSchema, localContext, args);
                task.Wait();
                model = task.Result;
            }

            // Deduplicate model instance on the current db context scope (if proxy).
            /* One document materializes one instance inside a scope: a full load of a document
             * with an already loaded instance returns the existing one, upgrading it in place
             * from summary with the fresh full model, if required. Models deserialized with the
             * no cache serializer modifier, outside of a scope, or without a compatible ambient
             * repository (e.g. projections of another model type on a raw collection read),
             * stay not deduplicated. */
            if (!dbContextEngine.SerializerModifierAccessor.IsNoCacheEnabled &&
                dbContextEngine.ProxyGenerator.IsProxyType(model!.GetType()) &&
                GetDocumentId(model, out var id, out _, out _) && id != null)
            {
                var currentDbContext = DbExecutionContextHandler.TryGetCurrentDbContext(dbContextEngine.ExecutionContext);
                var ambientRepository = DbExecutionContextHandler.TryGetCurrentRepository(dbContextEngine.ExecutionContext);
                if (currentDbContext is not null &&
                    ambientRepository is not null &&
                    ambientRepository.ModelType.IsAssignableFrom(typeof(TModel)))
                {
                    var internalDbContext = (IInternalDbContext)currentDbContext;
                    var loadedModel = currentDbContext.TryGetLoadedModel(ambientRepository, id);
                    if (loadedModel is null)
                    {
                        //capture the model document from the just deserialized document.
                        internalDbContext.RegisterLoadedModel(id, (IEntityModel)model);
                        internalDbContext.SetModelBsonDocument((IEntityModel)model, bsonDocument);
                    }
                    else if (loadedModel is TModel typedLoadedModel)
                    {
                        if (dbContextEngine.ProxyGenerator.PurgeProxyType(typedLoadedModel.GetType()) == actualType)
                        {
                            if (typedLoadedModel is IReferenceable { IsSummary: true } referenceableModel)
                                referenceableModel.MergeFullModel(model);
                            model = typedLoadedModel;
                        }
                        else
                        {
                            /* The document changed type after the loaded instance materialized,
                             * and an instance type can't upgrade: the full document read is
                             * authoritative, so the fresh instance replaces the outdated one as
                             * the loaded model, and is returned by this and the next loads. */
                            internalDbContext.ReplaceOutdatedLoadedModel(id, (IEntityModel)typedLoadedModel, (IEntityModel)model);
                            internalDbContext.SetModelBsonDocument((IEntityModel)model, bsonDocument);
                        }
                    }
                }
            }

            return model;
        }

        public bool GetDocumentId(object document, out object id, out Type idNominalType, out IIdGenerator idGenerator) =>
            DefaultBsonClassMapSerializer.GetDocumentId(document, out id, out idNominalType, out idGenerator);

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, TModel value)
        {
            ArgumentNullException.ThrowIfNull(context);

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
            /* Proxy types have no registered maps: a proxy instance serializes through the
             * model map of its purged type. Serializing as the nominal type keeps the bson
             * class map serializer on the purged class map, instead of delegating to the
             * actual type; member reads dispatch to the proxy overrides anyway. */
            var actualType = dbContextEngine.ProxyGenerator.PurgeProxyType(value.GetType());
            if (actualType != value.GetType())
                args.SerializeAsNominalType = true;
            var modelMap = dbContextEngine.MapRegistry.GetModelMap(actualType);

            // Serialize.
            modelMap.ActiveSchema.Serializer.Serialize(localContext, args, value);

            // Add additional data.
            //add model map schema id

            /* Verify if already exists, because if current model type is derived from the basic collection type,
             * the basic type serializer is called before, and a more specific serializer as been already invoked
             * from bson class map serializer. In that case, the right schema id has already been set, and we
             * don't have to replace it with the one wrong of the basic collection model type.
             */
            if (!bsonDocument.Contains(dbContextEngine.Options.ModelMapSchemaId.ElementName))
                bsonDocument.InsertAt(0, dbContextEngine.MapRegistry.GetActiveSchemaIdBsonElement(actualType));

            // Serialize document.
            BsonDocumentSerializer.Instance.Serialize(context, args, bsonDocument);
        }

        public void SetDocumentId(object document, object id) =>
            DefaultBsonClassMapSerializer.SetDocumentId(document, id);

        public bool TryGetMemberSerializationInfo(string memberName, out BsonSerializationInfo serializationInfo) =>
            DefaultBsonClassMapSerializer.TryGetMemberSerializationInfo(memberName, out serializationInfo);

        // Helpers.
        private static async Task<TModel> DeserializeModelMapSchemaHelperAsync(
            IModelMapSchema modelMapSchema,
            BsonDeserializationContext context,
            BsonDeserializationArgs args)
        {
            // If model map schema ask to override the nominal type, override it on args.
            var modelMapSchemaType = modelMapSchema.GetType();
            if (modelMapSchemaType.IsGenericType &&
                modelMapSchemaType.GetGenericTypeDefinition() == typeof(ModelMapSchema<,>))
                args = new BsonDeserializationArgs { NominalType = modelMapSchema.ModelType };

            // Deserialize.
            var model = (TModel)modelMapSchema.Serializer.Deserialize(context, args);

            // Fix model.
            model = (TModel)await modelMapSchema.FixDeserializedModelAsync(model).ConfigureAwait(false);

            // Clear extra elements.
            /* The fix is the consumer of the extra data: once executed, unmapped elements
             * would only be useless weight carried in memory by the loaded model. */
            if (model is IModel fixedModel)
                fixedModel.ExtraElements?.Clear();

            return model;
        }
    }
}
