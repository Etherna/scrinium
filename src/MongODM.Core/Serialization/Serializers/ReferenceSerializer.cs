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
using Etherna.MongODM.Core.ProxyModels;
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.Core.Serialization.Mapping;
using Etherna.MongODM.Core.Utility;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Etherna.MongODM.Core.Serialization.Serializers
{
    /// <summary>
    /// Typed factory of <see cref="ReferenceSerializer{TModelBase, TKey}"/> instances.
    /// </summary>
    public static class ReferenceSerializer
    {
        // Methods.
        /// <summary>
        /// Create a reference serializer declaring its source repository with a typed selector:
        /// generic arguments are inferred from the selector, and the source compatibility with
        /// the reference model and key types is verified at compile time.
        /// </summary>
        /// <typeparam name="TDbContext">Db context type hosting the source repository</typeparam>
        /// <typeparam name="TModelBase">Nominal model type</typeparam>
        /// <typeparam name="TKey">Model Id type</typeparam>
        public static ReferenceSerializer<TModelBase, TKey> Create<TDbContext, TModelBase, TKey>(
            IDbContextEngine dbContextEngine,
            Action<ReferenceSerializerConfiguration> configure,
            Func<TDbContext, IRepository<TModelBase, TKey>> sourceRepository)
            where TDbContext : class, IDbContext
            where TModelBase : class, IEntityModel<TKey>
        {
            ArgumentNullException.ThrowIfNull(sourceRepository);

            /* The typed selector requires the current scope db context as TDbContext: cast it
             * once here, failing with a detailed exception when the scope is a different db
             * context type. The engine build validation invokes the selector on the builder
             * db context instance, so a mismatching declared type still fails fast at startup.
             * Cross db context references will replace this cast with a db context locator
             * resolving the declared type (MODM-101). */
            return new(dbContextEngine, configure,
                sourceRepository: dbContext => sourceRepository(
                    dbContext as TDbContext ?? throw new InvalidOperationException(
                        $"Reference serializer of model type {typeof(TModelBase).Name} declares " +
                        $"its source repository on db context type {typeof(TDbContext).Name}, " +
                        $"not implemented by the current db context {dbContext.GetType().Name}")));
        }
    }

    /// <summary>
    /// Use ActiveModelMap.BsonClassMap definition in specific configuration to serialize reference documents.
    /// </summary>
    /// <typeparam name="TModelBase">Nominal model type</typeparam>
    /// <typeparam name="TKey">Model Id type</typeparam>
    public sealed class ReferenceSerializer<TModelBase, TKey> :
        SerializerBase<TModelBase>,
        IReferenceSerializer
        where TModelBase : class, IEntityModel<TKey>
    {
        // Fields.
        private readonly ReferenceSerializerConfiguration _configuration;
        private IDiscriminatorConvention _discriminatorConvention = null!;

        private readonly IDbContextEngine dbContextEngine;
        private Func<IDbContext, IRepository>? sourceRepositorySelector;

        // Constructor.
        public ReferenceSerializer(
            IDbContextEngine dbContextEngine,
            Action<ReferenceSerializerConfiguration> configure,
            Func<IDbContext, IRepository>? sourceRepository = null)
        {
            ArgumentNullException.ThrowIfNull(configure);

            this.dbContextEngine = dbContextEngine ?? throw new ArgumentNullException(nameof(dbContextEngine));
            sourceRepositorySelector = sourceRepository;

            /* Report the source declaration to the map registry for initialization
             * validation and resolution: implicit sources resolve at engine build to the
             * single compatible db context repository (ambiguity fails fast), declared
             * repositories are validated for compatibility at engine build.
             * Source references require the library map registry: a replaced registry
             * fails the cast loudly here, instead of silently skipping registration. */
            if (sourceRepository is null)
                ((MapRegistry)dbContextEngine.MapRegistry).AddImplicitSourceReference(this);
            else
                ((MapRegistry)dbContextEngine.MapRegistry).AddDeclaredSourceReference(typeof(TModelBase), typeof(TKey), sourceRepository);

            _configuration = new ReferenceSerializerConfiguration(dbContextEngine);
            configure(_configuration);
        }

        // Internal properties.
        Type IReferenceSerializer.ReferenceKeyType => typeof(TKey);
        Type IReferenceSerializer.ReferenceModelType => typeof(TModelBase);
        Func<IDbContext, IRepository>? IReferenceSerializer.SourceRepositorySelector
        {
            get => sourceRepositorySelector;
            set => sourceRepositorySelector = value;
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
                _discriminatorConvention ??= dbContextEngine.DiscriminatorRegistry.LookupDiscriminatorConvention(typeof(TModelBase));
                return _discriminatorConvention;
            }
        }

        // Methods.
        public override TModelBase Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            ArgumentNullException.ThrowIfNull(context);

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
            if (bsonDocument.TryGetElement(dbContextEngine.Options.ModelMapVersion.ElementName, out BsonElement modelMapIdElement))
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
            /* Push the source repository resolved for this reference member, when the
             * operation runs inside a db context scope: the created proxy binds to it.
             * Members without a resolvable source (references to models of another db
             * context) push a null repository, shadowing the outer operation one. */
            DbExecutionContextHandler? originDbExecContextHandler = null;
            if (DbExecutionContextHandler.TryGetCurrentDbContext(dbContextEngine.ExecutionContext) is { } originDbContext)
                originDbExecContextHandler = new DbExecutionContextHandler(originDbContext, sourceRepositorySelector?.Invoke(originDbContext));

            //get serializer
            TModelBase? model;
            try
            {
                var serializer = Configuration.GetSerializer(actualType, modelMapId);
                model = serializer.Deserialize(localContext, args) as TModelBase;
            }
            finally
            {
                originDbExecContextHandler?.Dispose();
            }

            // Process model (if proxy).
            /* Proxy models enable different features. Anyway, if the model as not been created as a proxy
             * (for example for tests scope) these additional operations are not possible or required.
             * In this case, simply return the model as is.
             */
            if (model != null &&
                dbContextEngine.ProxyGenerator.IsProxyType(model.GetType()))
            {
                var id = model.Id;
                if (id == null) //ignore refered instances without id
                    return null!;

                // Set model as summarizable.
                /* The id member never joins the summary member names: identity is
                 * definitionally present on any instance. */
                if (dbContextEngine.SerializerModifierAccessor.IsReadOnlyReferencedIdEnabled)
                {
                    ((IReferenceable)model).ClearSettedMembers();
                    ((IReferenceable)model).SetAsSummary([]);
                }
                else
                {
                    ((IReferenceable)model).SetAsSummary(((IReferenceable)model).SettedMemberNames);
                }

                // Deduplicate model instance on the current db context scope.
                /* A reference to an already loaded document returns the existing instance.
                 * The first loaded instance becomes the canonical one for its document, but a
                 * new summary can carry denormalized members that the loaded instance doesn't
                 * have yet: merge them instead of discarding the fresh deserialization. */
                if (!dbContextEngine.SerializerModifierAccessor.IsNoCacheEnabled)
                {
                    var currentDbContext = DbExecutionContextHandler.TryGetCurrentDbContext(dbContextEngine.ExecutionContext);
                    if (currentDbContext is not null)
                    {
                        var loadedModel = sourceRepositorySelector is not null ?
                            currentDbContext.TryGetLoadedModel(sourceRepositorySelector(currentDbContext), id) :
                            currentDbContext.TryGetLoadedModel(typeof(TModelBase), id);
                        if (loadedModel is null)
                        {
                            //capture the change tracking baseline from the just deserialized summary document.
                            currentDbContext.RegisterLoadedModel(id, model);
                            currentDbContext.SetModelBsonDocument(model, bsonDocument);
                        }
                        else if (loadedModel is TModelBase typedLoadedModel)
                        {
                            if (typedLoadedModel is IReferenceable { IsSummary: true } referenceableModel)
                                referenceableModel.MergeSummaryModel(model);
                            model = typedLoadedModel;
                        }
                    }
                }
            }

            return model!;
        }

        public bool GetDocumentId(object document, out object id, out Type idNominalType, out IIdGenerator idGenerator)
        {
            ArgumentNullException.ThrowIfNull(document);

            var serializer = Configuration.ModelMaps[dbContextEngine.ProxyGenerator.PurgeProxyType(document.GetType())].ActiveSchema.Serializer;

            if (serializer is IBsonIdProvider idProvider)
                return idProvider.GetDocumentId(document, out id, out idNominalType, out idGenerator);

            id = null!;
            idNominalType = null!;
            idGenerator = null!;
            return false;
        }

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, TModelBase value)
        {
            ArgumentNullException.ThrowIfNull(context);

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
            /* Proxy types have no registered maps: a proxy instance serializes through the
             * model map of its purged type, as the nominal type (see ModelMapSerializer). */
            var actualType = dbContextEngine.ProxyGenerator.PurgeProxyType(value.GetType());
            if (actualType != value.GetType())
                args.SerializeAsNominalType = true;
            var serializer = Configuration.ModelMaps[actualType].ActiveSchema.Serializer;
            serializer.Serialize(localContext, args, value);

            // Add additional data.
            //add model map id
            if (bsonDocument.Contains(dbContextEngine.Options.ModelMapVersion.ElementName))
                bsonDocument.Remove(dbContextEngine.Options.ModelMapVersion.ElementName);
            var modelMapIdElement = Configuration.GetActiveModelMapIdBsonElement(actualType);
            bsonDocument.InsertAt(0, modelMapIdElement);

            // Serialize document.
            BsonDocumentSerializer.Instance.Serialize(context, args, bsonDocument);
        }

        public void SetDocumentId(object document, object id)
        {
            ArgumentNullException.ThrowIfNull(document);

            var serializer = Configuration.ModelMaps[dbContextEngine.ProxyGenerator.PurgeProxyType(document.GetType())].ActiveSchema.Serializer;

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
