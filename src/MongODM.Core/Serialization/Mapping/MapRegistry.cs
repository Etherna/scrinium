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
using Etherna.MongODM.Core.Utility;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Etherna.MongODM.Core.Serialization.Mapping
{
    public class MapRegistry : FreezableConfig, IMapRegistry
    {
        // Fields.
        private readonly Dictionary<Type, IMap> _maps = new(); //model type -> map
        private readonly Dictionary<string, IMemberMap> _memberMapsById = new();

        private readonly Dictionary<Type, BsonElement> activeModelMapIdBsonElement = new();
        private IDbContextEngine dbContextEngine = null!;
        private ILogger logger = null!;
        private readonly Dictionary<IModelMap, Dictionary<string, List<IMemberMap>>> memberMapsByElementPath = new(); //model map -> element path -> member map[]
        private readonly Dictionary<MemberInfo, List<IMemberMap>> memberMapsByMemberInfo = new();

        // Constructor and initializer.
        public void Initialize(IDbContextEngine dbContextEngine, ILogger logger)
        {
            if (IsInitialized)
                throw new InvalidOperationException("Instance already initialized");
            this.dbContextEngine = dbContextEngine ?? throw new ArgumentNullException(nameof(dbContextEngine));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

            IsInitialized = true;

            this.logger.SchemaRegistryInitialized(dbContextEngine.Options.DbName);
        }

        // Properties.
        public bool IsInitialized { get; private set; }
        public IReadOnlyDictionary<Type, IMap> MapsByModelType => _maps;
        public IReadOnlyDictionary<string, IMemberMap> MemberMapsById => _memberMapsById;

        // Methods.
        public ICustomSerializerMapBuilder<TModel> AddCustomSerializerMap<TModel>(
            IBsonSerializer<TModel> customSerializer) =>
            ExecuteConfigAction(() =>
            {
                ArgumentNullException.ThrowIfNull(customSerializer);

                // Register and return map configuration.
                var customSerializerMap = new CustomSerializerMap<TModel>(customSerializer);
                _maps.Add(typeof(TModel), customSerializerMap);

                return customSerializerMap;
            });

        public IModelMapBuilder<TModel> AddModelMap<TModel>(
            string activeModelMapSchemaId,
            Action<BsonClassMap<TModel>>? activeModelMapSchemaInitializer = null) =>
            ExecuteConfigAction(() =>
            {
                // Register and add schema configuration.
                var modelMap = new ModelMap<TModel>(dbContextEngine);
                _maps.Add(typeof(TModel), modelMap);

                // Create model map and set it as active in schema.
                var schema = new ModelMapSchema<TModel>(
                    activeModelMapSchemaId,
                    new BsonClassMap<TModel>(activeModelMapSchemaInitializer ?? (cm => cm.AutoMap())),
                    null,
                    null,
                    modelMap);
                modelMap.ActiveSchema = schema;

                // If model schema uses proxy model, register a new one for proxy type.
                if (modelMap.ProxyModelType != null)
                {
                    var proxyModelMap = CreateNewDefaultModelMap(modelMap.ProxyModelType);
                    _maps.Add(modelMap.ProxyModelType, proxyModelMap);
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

        public IBsonSerializer GetMappedSerializer(Type modelType)
        {
            ArgumentNullException.ThrowIfNull(modelType);
            
            if (!_maps.TryGetValue(modelType, out var map))
                throw new KeyNotFoundException(modelType.Name + " map is missing");

            return map.Serializer;
        }

        public IEnumerable<IMemberMap> GetMemberMapsFromMemberInfo(MemberInfo memberInfo)
        {
            Freeze(); //needed for initialization
            return memberMapsByMemberInfo.FirstOrDefault(p => p.Key.IsSameAs(memberInfo)).Value ??
                (IEnumerable<IMemberMap>)[];
        }

        public IEnumerable<IMemberMap> GetMemberMapsWithSameElementPath(IMemberMap memberMap)
        {
            ArgumentNullException.ThrowIfNull(memberMap);
            
            Freeze(); //needed for initialization
            return memberMapsByElementPath.TryGetValue(memberMap.MemberMapPath.First().ModelMapSchema.ModelMap, out var elementPathDictionary) &&
                elementPathDictionary.TryGetValue(GetMemberMapElementPath(memberMap), out var samePathMemberMaps) ?
                samePathMemberMaps :
                Array.Empty<IMemberMap>();
        }

        public IModelMap GetModelMap(Type modelType)
        {
            ArgumentNullException.ThrowIfNull(modelType);
            
            if (!_maps.TryGetValue(modelType, out var map))
                throw new KeyNotFoundException(modelType.Name + " map is missing");

            if (map is not IModelMap modelMap)
                throw new InvalidOperationException(modelType.Name + " map is not a model map");

            return modelMap;
        }

        public bool TryGetMappedSerializer(Type modelType, out IBsonSerializer serializer)
        {
            ArgumentNullException.ThrowIfNull(modelType);

            if (_maps.TryGetValue(modelType, out var map))
            {
                serializer = map.Serializer;
                return true;
            }

            serializer = null!;
            return false;
        }

        public bool TryGetModelMap(Type modelType, out IModelMap modelMap)
        {
            ArgumentNullException.ThrowIfNull(modelType);

            if (_maps.TryGetValue(modelType, out var map) &&
                map is IModelMap foundModelMap)
            {
                modelMap = foundModelMap;
                return true;
            }

            modelMap = null!;
            return false;
        }

        // Protected methods.
        protected override void FreezeAction()
        {
            // Link model maps with their base map.
            LinkBaseModelMaps();

            // Freeze, register serializers and compile registers.
            foreach (var map in _maps.Values)
            {
                // Freeze model map.
                map.Freeze();

                // Register active serializer.
                ((BsonSerializerRegistry)dbContextEngine.SerializerRegistry).RegisterSerializer(map.ModelType, map.Serializer);

                // Register discriminators for all bson class maps.
                if (map is IModelMap modelMap)
                    foreach (var modelMapSchema in modelMap.SchemasById.Values)
                        dbContextEngine.DiscriminatorRegistry.AddDiscriminator(modelMapSchema.ModelType, modelMapSchema.Discriminator);
            }

            // Specific for model maps.
            foreach (var modelMap in _maps.Values.OfType<ModelMap>())
            {
                // Initialize member maps.
                modelMap.InitializeMemberMaps();

                // Initialize member map registers.
                /*
                 * Only model map based schemas can be analyzed.
                 * Schemas based on custom serializers can't be explored.
                 * 
                 * Skip member map analysis of proxy models.
                 * 
                 * This operation needs to be executed AFTER that all serializers have been registered.
                 */
                if (!dbContextEngine.ProxyGenerator.IsProxyType(modelMap.ModelType))
                {
                    foreach (var memberMap in modelMap.AllDescendingMemberMaps)
                    {
                        //map member map into registers
                        _memberMapsById[memberMap.Id] = memberMap;
                        MapMemberMapsByMemberInfo(memberMap);
                        MapMemberMapsByRootModelMapAndElementPath(memberMap);
                    }
                }

                // Generate active model maps id bson elements.
                /*
                 * If current model type is proxy, we need to use id of its base type. This because
                 * when we serialize a proxy model, we don't want that the proxy's model map id
                 * will be reported on document, but we want to serialize its original type's id.
                 */
                var notProxyModelMap = GetModelMap(dbContextEngine.ProxyGenerator.PurgeProxyType(modelMap.ModelType));

                activeModelMapIdBsonElement.Add(
                    modelMap.ModelType,
                    new BsonElement(
                        dbContextEngine.Options.ModelMapVersion.ElementName,
                        new BsonString(notProxyModelMap.ActiveSchema.Id)));
            }
        }

        // Helpers.
        private ModelMap CreateNewDefaultModelMap(Type modelType)
        {
            // Construct.
            //model schema
            var modelMapDefinition = typeof(ModelMap<>);
            var modelMapType = modelMapDefinition.MakeGenericType(modelType);

            var modelMap = (ModelMap)Activator.CreateInstance(
                modelMapType,
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
                    null!,                     //Func<TModel, Task<TModel>>? fixDeserializedModelFunc
                    modelMap                   //IModelMap modelMap
                ],
                CultureInfo.InvariantCulture)!;

            // Set active model map.
            modelMap.ActiveSchema = activeModelMapSchema;

            return modelMap;
        }

        private static string GetMemberMapElementPath(IMemberMap memberMap) => memberMap.RenderElementPath(false, _ => ".$", _ => ".*");

        private void LinkBaseModelMaps()
        {
            /* A stack with a while iteration is needed, instead of a foreach construct,
             * because we will add new schemas if needed. Foreach is based on enumerable
             * iterator, and if an enumerable is modified during foreach execution, an
             * exception is rised.
             */
            var processingModelMaps = new Stack<IModelMap>(_maps.Values.OfType<IModelMap>());

            while (processingModelMaps.Count != 0)
            {
                var modelMap = processingModelMaps.Pop();

                // Process schema's model maps.
                foreach (var modelMapSchema in modelMap.SchemasById.Values)
                {
                    var baseModelType = modelMapSchema.ModelType.BaseType;

                    // If don't need to be linked, because it is typeof(object).
                    if (baseModelType is null)
                        continue;

                    // Get base type map, or generate it.
                    if (!_maps.TryGetValue(baseModelType, out IMap? baseMap))
                    {
                        // Create schema instance.
                        baseMap = CreateNewDefaultModelMap(baseModelType);

                        // Register schema instance.
                        _maps.Add(baseModelType, baseMap);
                        processingModelMaps.Push((IModelMap)baseMap);
                    }

                    // Search base model map schema.
                    var baseModelMapSchema = modelMapSchema.BaseSchemaId != null ?
                        ((IModelMap)baseMap).SchemasById[modelMapSchema.BaseSchemaId] :
                        ((IModelMap)baseMap).ActiveSchema;

                    // Link base model map.
                    modelMapSchema.SetBaseModelMapSchema(baseModelMapSchema);
                }
            }
        }

        private void MapMemberMapsByMemberInfo(IMemberMap memberMap)
        {
            /*
             * MemberInfo comparison has to be performed with extension method "IsSameAs". If an equal member info
             * is found with this equality comparer, it has to be taken as key also for current memberinfo
             */
            var memberInfo = memberMap.BsonMemberMap.MemberInfo;
            var memberMapListByMemberInfo = memberMapsByMemberInfo.FirstOrDefault(pair => pair.Key.IsSameAs(memberInfo)).Value;

            if (memberMapListByMemberInfo is null)
            {
                memberMapListByMemberInfo = new List<IMemberMap>();
                memberMapsByMemberInfo[memberInfo] = memberMapListByMemberInfo;
            }

            memberMapListByMemberInfo.Add(memberMap);
        }

        private void MapMemberMapsByRootModelMapAndElementPath(IMemberMap memberMap)
        {
            var rootModelMap = memberMap.MemberMapPath.First().ModelMapSchema.ModelMap;
            var memberMapElementPath = GetMemberMapElementPath(memberMap);
            if (!memberMapsByElementPath.TryGetValue(rootModelMap, out var memberMapDictionaryByElementPath))
            {
                memberMapDictionaryByElementPath = new Dictionary<string, List<IMemberMap>>();
                memberMapsByElementPath[rootModelMap] = memberMapDictionaryByElementPath;
            }
            if (!memberMapDictionaryByElementPath.TryGetValue(memberMapElementPath, out var memberMapListByElementPath))
            {
                memberMapListByElementPath = new List<IMemberMap>();
                memberMapDictionaryByElementPath[memberMapElementPath] = memberMapListByElementPath;
            }

            memberMapListByElementPath.Add(memberMap);
        }
    }
}
