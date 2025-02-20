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
using System.Linq;
using System.Threading;

namespace Etherna.MongODM.Core.Serialization.Serializers
{
    /// <summary>
    /// Use ActiveModelMap.BsonClassMap definition in specific configuration to serialize reference documents.
    /// </summary>
    /// <typeparam name="TModelBase">Nominal model type</typeparam>
    /// <typeparam name="TKey">Model Id type</typeparam>
    public class ReferenceSerializer<TModelBase, TKey> :
        SerializerBase<TModelBase>,
        IDisposable,
        IReferenceSerializer
        where TModelBase : class, IEntityModel<TKey>
    {
        // Fields.
        private readonly ReferenceSerializerConfiguration _configuration;
        private IDiscriminatorConvention _discriminatorConvention = default!;

        private readonly ReaderWriterLockSlim configLockAdapters = new(LockRecursionPolicy.SupportsRecursion);
        private readonly IDbContext dbContext;
        private bool disposed;

        // Constructor.
        public ReferenceSerializer(
            IDbContext dbContext,
            Action<ReferenceSerializerConfiguration> configure)
        {
            ArgumentNullException.ThrowIfNull(configure, nameof(configure));

            this.dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

            _configuration = new ReferenceSerializerConfiguration(dbContext);
            configure(_configuration);
        }

        // Dispose.
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed) return;

            // Dispose managed resources.
            if (disposing)
            {
                configLockAdapters.Dispose();
                _configuration.Dispose();
            }

            disposed = true;
        }

        // Properties.
        public IEnumerable<IModelMap> HandledModelMaps => Configuration.ModelMaps.Values;

        public ReferenceSerializerConfiguration Configuration
        {
            get
            {
                _configuration.Freeze();
                return _configuration;
            }
        }

        public IDiscriminatorConvention DiscriminatorConvention
        {
            get
            {
                _discriminatorConvention ??= dbContext.DiscriminatorRegistry.LookupDiscriminatorConvention(typeof(TModelBase));
                return _discriminatorConvention;
            }
        }

        // Methods.
        public override TModelBase Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            ArgumentNullException.ThrowIfNull(context, nameof(context));

            // Check bson type.
            var bsonType = context.Reader.GetCurrentBsonType();
            switch (bsonType)
            {
                case BsonType.Document:
                    break;
                case BsonType.Null:
                    context.Reader.ReadNull();
                    return null!;
                default:
                    var message = $"Expected a nested document representing the serialized form of a {nameof(TModelBase)} value, but found a value of type {bsonType} instead.";
                    throw new InvalidOperationException(message);
            }

            // Find pre-deserialization information.
            //get actual type
            var actualType = DiscriminatorConvention.GetActualType(context.Reader, args.NominalType);

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
            //get serializer
            var serializer = Configuration.GetSerializer(actualType, modelMapId);
            var model = serializer.Deserialize(localContext, args) as TModelBase;

            // Process model (if proxy).
            /* Proxy models enable different features. Anyway, if the model as not been created as a proxy
             * (for example for tests scope) these additional operations are not possible or required.
             * In this case, simply return the model as is.
             */
            if (model != null &&
                dbContext.ProxyGenerator.IsProxyType(model.GetType()))
            {
                var id = model.Id;
                if (id == null) //ignore refered instances without id
                    return null!;

                // Check if model as been loaded in cache.
                if (dbContext.DbCache.LoadedModels.ContainsKey(id) &&
                    !dbContext.SerializerModifierAccessor.IsNoCacheEnabled)
                {
                    var cachedModel = (TModelBase)dbContext.DbCache.LoadedModels[id];

                    if (((IReferenceable)cachedModel).IsSummary)
                    {
                        // Execute merging between summary models.
                        var sourceMembers = ((IReferenceable)model).SettedMemberNames
                            .Except(((IReferenceable)cachedModel).SettedMemberNames)
                            .Select(memberName => cachedModel.GetType().GetMember(memberName).Single())
                            .ToArray();

                        //temporary disable auditing
                        ((IAuditable)cachedModel).DisableAuditing();

                        foreach (var member in sourceMembers)
                        {
                            var value = ReflectionHelper.GetValue(model, member);
                            ReflectionHelper.SetValue(cachedModel, member, value);
                        }

                        //reenable auditing
                        ((IAuditable)cachedModel).EnableAuditing();

                        ((IReferenceable)cachedModel).SetAsSummary(sourceMembers.Select(m => m.Name));
                    }

                    // Return the cached model.
                    model = cachedModel;
                }
                else
                {
                    // Set model as summarizable.
                    if (dbContext.SerializerModifierAccessor.IsReadOnlyReferencedIdEnabled)
                    {
                        ((IReferenceable)model).ClearSettedMembers();
                        ((IReferenceable)model).SetAsSummary(new[] { nameof(model.Id) });
                    }
                    else
                    {
                        ((IReferenceable)model).SetAsSummary(((IReferenceable)model).SettedMemberNames);
                    }

                    // Enable auditing.
                    ((IAuditable)model).EnableAuditing();

                    // Add in cache.
                    if (!dbContext.SerializerModifierAccessor.IsNoCacheEnabled)
                        dbContext.DbCache.AddModel(model.Id!, model);
                }
            }

            return model!;
        }

        public bool GetDocumentId(object document, out object id, out Type idNominalType, out IIdGenerator idGenerator)
        {
            ArgumentNullException.ThrowIfNull(document, nameof(document));

            var serializer = Configuration.ModelMaps[document.GetType()].ActiveSchema.Serializer;

            if (serializer is IBsonIdProvider idProvider)
                return idProvider.GetDocumentId(document, out id, out idNominalType, out idGenerator);

            id = null!;
            idNominalType = null!;
            idGenerator = null!;
            return false;
        }

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, TModelBase value)
        {
            ArgumentNullException.ThrowIfNull(context, nameof(context));

            // Check value type.
            if (value == null)
            {
                context.Writer.WriteNull();
                return;
            }

            // Clear extra elements. They are never needed with references.
            value.ExtraElements?.Clear();

            // Initialize localContext, bsonDocument and bsonWriter.
            var bsonDocument = new BsonDocument();
            using var bsonWriter = new BsonDocumentWriter(bsonDocument);
            var localContext = BsonSerializationContext.CreateRoot(
                bsonWriter,
                builder => builder.IsDynamicType = context.IsDynamicType);

            // Serialize.
            var serializer = Configuration.ModelMaps[value.GetType()].ActiveSchema.Serializer;
            serializer.Serialize(localContext, args, value);

            // Add additional data.
            //add model map id
            if (bsonDocument.Contains(dbContext.Options.ModelMapVersion.ElementName))
                bsonDocument.Remove(dbContext.Options.ModelMapVersion.ElementName);
            var modelMapIdElement = Configuration.GetActiveModelMapIdBsonElement(
                dbContext.ProxyGenerator.PurgeProxyType(value.GetType()));
            bsonDocument.InsertAt(0, modelMapIdElement);

            // Serialize document.
            BsonDocumentSerializer.Instance.Serialize(context, args, bsonDocument);
        }

        public void SetDocumentId(object document, object id)
        {
            ArgumentNullException.ThrowIfNull(document, nameof(document));

            var serializer = Configuration.ModelMaps[document.GetType()].ActiveSchema.Serializer;

            if (serializer is IBsonIdProvider idProvider)
                idProvider.SetDocumentId(document, id);
            else
                throw new InvalidOperationException("Can't find a valid serializer");
        }

        public bool TryGetMemberSerializationInfo(string memberName, out BsonSerializationInfo serializationInfo)
        {
            var schema = Configuration.ModelMaps.Values.FirstOrDefault(
                mm => mm.ActiveSchema.TryGetMemberMap(memberName) != null);

            if (schema?.Serializer is IBsonDocumentSerializer documentSerializer)
                return documentSerializer.TryGetMemberSerializationInfo(memberName, out serializationInfo);
            
            serializationInfo = null!;
            return false;
        }

        // Helpers.
        private static string? BsonValueToModelMapId(BsonValue bsonValue) =>
            bsonValue switch
            {
                BsonNull _ => null,
                BsonString bsonString => bsonString.AsString,
                _ => throw new NotSupportedException(),
            };
    }
}
