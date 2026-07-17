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
using Etherna.MongODM.Core.Serialization.Mapping;
using Etherna.MongODM.Core.Utility;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace Etherna.MongODM.Core.Serialization.Serializers
{
    public class ReferenceSerializerConfiguration : FreezableConfig
    {
        // Fields.
        private readonly Dictionary<Type, IModelMap> _modelMaps = new();

        private readonly Dictionary<Type, BsonElement> activeModelMapIdBsonElement = new();
        private readonly IDbContextEngine dbContextEngine;

        // Constructor.
        internal ReferenceSerializerConfiguration(IDbContextEngine dbContextEngine)
        {
            this.dbContextEngine = dbContextEngine;
        }

        // Properties.
        public IReadOnlyDictionary<Type, IModelMap> ModelMaps => _modelMaps;

        // Methods.
        public IModelMapBuilder<TModel> AddModelMap<TModel>(
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

                // If model schema uses proxy model, register a new one for proxy type.
                if (modelMap.ProxyModelType != null)
                {
                    var proxyModelMap = CreateNewDefaultModelMap(modelMap.ProxyModelType);
                    _modelMaps.Add(modelMap.ProxyModelType, proxyModelMap);
                }

                return modelMap;
            });

        public BsonElement GetActiveModelMapIdBsonElement(Type modelType)
        {
            ArgumentNullException.ThrowIfNull(modelType);

            Freeze(); //needed for initialization

            /*
             * Use of this cache dictionary avoids checks and creation of new bson elements
             * for each serialization.
             */
            return activeModelMapIdBsonElement[modelType];
        }

        public IBsonSerializer GetSerializer(Type modelType, string? modelMapSchemaId)
        {
            ArgumentNullException.ThrowIfNull(modelType);
            if (!_modelMaps.TryGetValue(modelType, out var modelMap))
                throw new InvalidOperationException("Can't identify registered schema for type " + modelType.Name);

            // Find serializer.
            //if a correct model map is identified with its id, use its bson class map serializer
            if (modelMapSchemaId != null && modelMap.SchemasById.TryGetValue(modelMapSchemaId, out var modelMapSchema))
                return modelMapSchema.Serializer;

            //else, use fallback serializer if exists, or the active schema's bsonClassMap serializer
            return modelMap.FallbackSerializer ?? modelMap.ActiveSchema.Serializer;
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

                // Generate active model maps id bson elements.
                /*
                 * If current model type is proxy, we need to use id of its base type. This because
                 * when we serialize a proxy model, we don't want that in the proxy's model map id
                 * will be reported on document, but we want to serialize its original type's id.
                 */
                var notProxySchema = _modelMaps[dbContextEngine.ProxyGenerator.PurgeProxyType(modelMap.ModelType)];

                activeModelMapIdBsonElement.Add(
                    modelMap.ModelType,
                    new BsonElement(
                        dbContextEngine.Options.ModelMapVersion.ElementName,
                        new BsonString(notProxySchema.ActiveSchema.Id)));
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
