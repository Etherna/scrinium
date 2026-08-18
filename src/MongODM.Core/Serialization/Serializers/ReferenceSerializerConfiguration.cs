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
using Etherna.MongoDB.Bson.Serialization;
using Etherna.MongODM.Core.Extensions;
using Etherna.MongODM.Core.Options;
using Etherna.MongODM.Core.Serialization.Mapping;
using Etherna.MongODM.Core.Utility;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Etherna.MongODM.Core.Serialization.Serializers
{
    public class ReferenceSerializerConfiguration : FreezableConfig
    {
        // Consts.
        /// <summary>
        /// Maximum number of distinct unrecognized model map schema ids reported by a reference
        /// serializer configuration. Schema ids come from documents, so the already reported ones
        /// can't be remembered without a bound.
        /// </summary>
        public const int MaxWarnedUnrecognizedSchemaIds = 100;

        // Fields.
        private MissingOriginDocumentMode _missingOriginDocument = MissingOriginDocumentMode.Throw;
        private readonly Dictionary<Type, IModelMap> _modelMaps = new();

        private readonly Dictionary<Type, BsonElement> activeSchemaIdBsonElement = new();
        private readonly IDbContextEngine dbContextEngine;
        private readonly Dictionary<Type, IBsonSerializer> defaultFallbackSerializers = [];
        private readonly HashSet<(Type ModelType, string? SchemaId)> warnedUnrecognizedSchemaIds = [];

        // Constructor.
        internal ReferenceSerializerConfiguration(IDbContextEngine dbContextEngine)
        {
            this.dbContextEngine = dbContextEngine;
        }

        // Properties.
        /// <summary>
        /// How the summary models deserialized by this reference react to a full load finding
        /// no origin document, because the referred document doesn't exist anymore on the
        /// origin collection. Denied by default: an inconsistency of the database would
        /// otherwise degrade the summary into a model carrying its not loaded members at their
        /// default values, without any report.
        /// </summary>
        public MissingOriginDocumentMode MissingOriginDocument
        {
            get => _missingOriginDocument;
            set => ExecuteConfigAction(() => _missingOriginDocument = value);
        }

        public IReadOnlyDictionary<Type, IModelMap> ModelMaps => _modelMaps;

        // Methods.
        public IReferenceModelMapBuilder<TModel> AddModelMap<TModel>(
            string activeModelMapSchemaId,
            Action<BsonClassMap<TModel>>? activeModelMapSchemaInitializer = null,
            string? baseSchemaId = null)
            where TModel : class =>
            ExecuteConfigAction(() =>
            {
                // Register and return schema configuration.
                var modelMap = new ModelMap<TModel>(dbContextEngine);
                _modelMaps.Add(typeof(TModel), modelMap);

                // Create model map and set it as active in schema.
                var modelMapSchema = new ModelMapSchema<TModel>(
                    activeModelMapSchemaId,
                    new BsonClassMap<TModel>(activeModelMapSchemaInitializer ?? (cm => cm.AutoMap())),
                    baseSchemaId,
                    null,
                    modelMap);
                modelMap.ActiveSchema = modelMapSchema;

                return modelMap;
            });

        public BsonElement GetActiveSchemaIdBsonElement(Type modelType)
        {
            ArgumentNullException.ThrowIfNull(modelType);

            Freeze(); //needed for initialization

            /*
             * Use of this cache dictionary avoids checks and creation of new bson elements
             * for each serialization.
             */
            return activeSchemaIdBsonElement[modelType];
        }

        public IBsonSerializer GetSerializer(Type modelType, string? modelMapSchemaId)
        {
            ArgumentNullException.ThrowIfNull(modelType);

            Freeze(); //needed for initialization

            if (!_modelMaps.TryGetValue(modelType, out var modelMap))
                throw new InvalidOperationException("Can't identify registered schema for type " + modelType.Name);

            // Find serializer.
            //if a correct model map schema is identified with its id, use its bson class map serializer
            if (modelMapSchemaId is not null && modelMap.SchemasById.TryGetValue(modelMapSchemaId, out var modelMapSchema))
                return modelMapSchema.Serializer;

            //else, use the configured fallback serializer or model map schema, if any exists
            if (modelMap.FallbackSerializer is not null)
                return modelMap.FallbackSerializer;
            if (modelMap.FallbackSchema is not null)
                return modelMap.FallbackSchema.Serializer;

            //else, deserialize only the reference id: any other member can lazy load from the origin document
            /* The schema id is document content: an id matching no registered schema, with no
             * fallback declared for it, degrades the read to the reference id alone, and every
             * member access of the resulting summary then lazy loads the whole origin document.
             * Report it once per model type and id, so the load amplification of an unexpected
             * value doesn't stay silent, up to the reported ids bound. */
            bool firstOccurrence;
            lock (warnedUnrecognizedSchemaIds)
                firstOccurrence = warnedUnrecognizedSchemaIds.Count < MaxWarnedUnrecognizedSchemaIds &&
                                  warnedUnrecognizedSchemaIds.Add((modelType, modelMapSchemaId));
            if (firstOccurrence)
                dbContextEngine.Logger.ReferenceSerializerUnrecognizedSchemaId(
                    dbContextEngine.Options.DbName, modelType.Name, modelMapSchemaId);

            return defaultFallbackSerializers[modelType];
        }

        /// <summary>
        /// Derive the summary loaded member names carried by a reference document, mapping its
        /// element names through the member maps of the schema deserializing it, resolved with
        /// the same fallback chain of <see cref="GetSerializer"/>. The id member never joins the
        /// summary member names. Returns null with a custom fallback serializer, which has no
        /// schema mapping elements to members.
        /// </summary>
        public IEnumerable<string>? TryGetSummaryLoadedMemberNames(
            Type modelType,
            string? modelMapSchemaId,
            BsonDocument referenceDocument)
        {
            ArgumentNullException.ThrowIfNull(modelType);
            ArgumentNullException.ThrowIfNull(referenceDocument);

            Freeze(); //needed for initialization

            if (!_modelMaps.TryGetValue(modelType, out var modelMap))
                throw new InvalidOperationException("Can't identify registered schema for type " + modelType.Name);

            //if a correct model map schema is identified with its id, map document elements with its member maps
            if (modelMapSchemaId is not null && modelMap.SchemasById.TryGetValue(modelMapSchemaId, out var modelMapSchema))
                return GetSummaryLoadedMemberNamesHelper(modelMapSchema, referenceDocument);

            //else, a custom fallback serializer deserializes without a schema mapping elements to members
            if (modelMap.FallbackSerializer is not null)
                return null;

            //else, map document elements with the configured fallback model map schema, if any exists
            if (modelMap.FallbackSchema is not null)
                return GetSummaryLoadedMemberNamesHelper(modelMap.FallbackSchema, referenceDocument);

            //else, the default fallback serializer reads only the reference id
            return [];
        }

        // Protected methods.
        protected override void FreezeAction()
        {
            // Link model maps with their base map.
            LinkBaseModelMaps();

            // Freeze and register bson elements.
            foreach (var modelMap in _modelMaps.Values)
            {
                // Freeze model map.
                modelMap.Freeze();

                // Generate active schema id bson elements.
                activeSchemaIdBsonElement.Add(
                    modelMap.ModelType,
                    new BsonElement(
                        dbContextEngine.Options.ModelMapSchemaId.ElementName,
                        new BsonString(modelMap.ActiveSchema.Id)));

                // Generate default fallback serializers.
                /* Reference documents with an unrecognized schema id deserialize reading only
                 * the id member of the active schema: being references, any other member can
                 * lazy load from the origin document. */
                var idBsonMemberMap = modelMap.ActiveSchema.AllMemberMaps.FirstOrDefault(mm => mm.IsIdMember());
                defaultFallbackSerializers.Add(
                    modelMap.ModelType,
                    new ReferenceFallbackSerializer(modelMap, idBsonMemberMap?.ElementName));
            }
        }

        // Helpers
        private ModelMap CreateNewDefaultModelMap(Type modelType)
        {
            //model schema
            var modelSchemaDefinition = typeof(ModelMap<>);
            var modelSchemaType = modelSchemaDefinition.MakeGenericType(modelType);

            var modelSchema = (ModelMap)Activator.CreateInstance(
                modelSchemaType,
                dbContextEngine)!;          //IDbContextEngine dbContextEngine

            //class map
            var classMapDefinition = typeof(BsonClassMap<>);
            var classMapType = classMapDefinition.MakeGenericType(modelType);

            var classMap = (BsonClassMap)Activator.CreateInstance(classMapType)!;

            //model map
            var modelMapSchemaDefinition = typeof(ModelMapSchema<>);
            var modelMapSchemaType = modelMapSchemaDefinition.MakeGenericType(modelType);

            var activeModelMapSchema = (ModelMapSchema)Activator.CreateInstance(
                modelMapSchemaType,
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                [
                    Guid.NewGuid().ToString(), //string id
                    classMap,                  //BsonClassMap<TModel> bsonClassMap
                    null!,                     //string? baseSchemaId
                    null!,                     //Func<IDbContext, TModel, Task<TModel>>? fixDeserializedModelFunc
                    modelSchema                //IModelSchema schema
                ],
                CultureInfo.InvariantCulture)!;

            // Set active model map.
            modelSchema.ActiveSchema = activeModelMapSchema;

            return modelSchema;
        }

        private static IEnumerable<string> GetSummaryLoadedMemberNamesHelper(
            IModelMapSchema modelMapSchema,
            BsonDocument referenceDocument) =>
            modelMapSchema.AllMemberMaps
                .Where(mm => !mm.IsIdMember() &&
                             mm != modelMapSchema.ExtraElementsMemberMap &&
                             referenceDocument.Contains(mm.ElementName))
                .Select(mm => mm.MemberName);

        private void LinkBaseModelMaps()
        {
            /* A stack with a while iteration is needed, instead of a foreach construct,
             * because we will add new schemas if needed. Foreach is based on enumerable
             * iterator, and if an enumerable is modified during foreach execution, an
             * exception is rised.
             */
            var processingModelMaps = new Stack<IModelMap>(_modelMaps.Values);

            while (processingModelMaps.Count != 0)
            {
                var modelMap = processingModelMaps.Pop();
                var baseModelType = modelMap.ModelType.BaseType;

                // If don't need to be linked, because it is typeof(object).
                if (baseModelType is null)
                    continue;

                // Get base type schema, or generate it.
                if (!_modelMaps.TryGetValue(baseModelType, out IModelMap? baseModelMap))
                {
                    // Create schema instance.
                    baseModelMap = CreateNewDefaultModelMap(baseModelType);

                    // Register schema instance.
                    _modelMaps.Add(baseModelType, baseModelMap);
                    processingModelMaps.Push(baseModelMap);
                }

                // Process model maps' schemas.
                foreach (var modelMapSchema in modelMap.SchemasById.Values)
                {
                    // Search base model map.
                    var baseModelMapSchema = modelMapSchema.BaseSchemaId != null ?
                        baseModelMap.SchemasById[modelMapSchema.BaseSchemaId] :
                        baseModelMap.ActiveSchema;

                    // Link base model map.
                    modelMapSchema.SetBaseModelMapSchema(baseModelMapSchema);
                }
            }
        }
    }
}
