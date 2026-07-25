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
using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.Extensions;
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.Core.Serialization.Serializers;
using Etherna.MongODM.Core.Exceptions;
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
        private readonly List<(Type ModelType, Type KeyType, Func<IDbContext, IRepository> Selector)> declaredSourceReferences = new();
        private readonly List<IReferenceSerializer> implicitSourceReferences = new();
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
        internal void AddDeclaredSourceReference(Type modelType, Type keyType, Func<IDbContext, IRepository> selector)
        {
            lock (declaredSourceReferences)
                declaredSourceReferences.Add((modelType, keyType, selector));
        }

        internal void AddImplicitSourceReference(IReferenceSerializer serializer)
        {
            lock (implicitSourceReferences)
                implicitSourceReferences.Add(serializer);
        }

        /* Reference serializers declaring a source repository must declare a compatible
         * one: hosting the reference model type, with the same key type. Selectors need a
         * db context instance to access its repository properties: the engine builder
         * provides its own, validating at initialization, before any use. */
        internal void ValidateDeclaredSourceReferences(IDbContext dbContext)
        {
            var violations = new List<string>();
            lock (declaredSourceReferences)
            {
                foreach (var (modelType, keyType, selector) in declaredSourceReferences)
                {
                    var repository = selector(dbContext);
                    if (!repository.ModelType.IsAssignableFrom(modelType))
                        violations.Add($"reference serializer of model type {modelType.Name} declares source repository {repository.Name}, handling the incompatible model type {repository.ModelType.Name}");
                    else if (repository.KeyType != keyType)
                        violations.Add($"reference serializer of model type {modelType.Name} with key type {keyType.Name} declares source repository {repository.Name}, handling the incompatible key type {repository.KeyType.Name}");
                }
            }

            if (violations.Count > 0)
                throw new MongodmInvalidEntityTypeException(
                    $"DbContext {dbContextEngine.DbContextType.Name} has reference serializers declaring incompatible source repositories: " +
                    string.Join("; ", violations));
        }

        protected override void FreezeAction()
        {
            // Verify uniqueness of model map schema ids.
            ValidateSchemaIds();

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
                 * This operation needs to be executed AFTER that all serializers have been registered.
                 */
                foreach (var memberMap in modelMap.AllDescendingMemberMaps)
                {
                    //map member map into registers
                    _memberMapsById[memberMap.Id] = memberMap;
                    MapMemberMapsByMemberInfo(memberMap);
                    MapMemberMapsByRootModelMapAndElementPath(memberMap);
                }

                // Generate active model maps id bson elements.
                activeModelMapIdBsonElement.Add(
                    modelMap.ModelType,
                    new BsonElement(
                        dbContextEngine.Options.ModelMapVersion.ElementName,
                        new BsonString(modelMap.ActiveSchema.Id)));
            }

            // Verify that mapped id members implement the entity id contract.
            ValidateIdMemberMaps();
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
                    null!,                     //Func<IDbContext, TModel, Task<TModel>>? fixDeserializedModelFunc
                    modelMap                   //IModelMap modelMap
                ],
                CultureInfo.InvariantCulture)!;

            // Set active model map.
            modelMap.ActiveSchema = activeModelMapSchema;

            return modelMap;
        }

        private static string GetMemberMapElementPath(IMemberMap memberMap) => memberMap.RenderElementPath(false, _ => ".$", _ => ".*");

        /* Reference serializers without a declared source repository deduce it from their
         * model and key types: resolve them at engine build to the single compatible
         * db context repository property, failing fast with the involved repositories
         * detail when the deduction is ambiguous. References without any compatible
         * repository (models of another db context) stay unresolved and unbound.
         * The builder instance gives access to the repository property values, exposing
         * their model and key types directly. */
        internal void ResolveImplicitSourceReferences(IDbContext dbContext)
        {
            /* Map the repository properties declared by the db context type. Filter by the
             * property type before reading values: getters of other db context properties
             * can throw on the not yet attached builder instance. */
            var repositoryPropertiesByModelType = dbContextEngine.DbContextType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                .Where(property => typeof(IRepository).IsAssignableFrom(property.PropertyType))
                .Select(property => (property, repository: property.GetValue(dbContext) as IRepository))
                .Where(pair => pair.repository is not null)
                .GroupBy(pair => pair.repository!.ModelType)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(pair => (pair.property, keyType: pair.repository!.KeyType)).ToArray());

            // Resolve each implicit reference to its single compatible repository property.
            var violations = new List<string>();
            var violatedModelTypes = new HashSet<Type>();
            lock (implicitSourceReferences)
            {
                foreach (var serializer in implicitSourceReferences)
                {
                    var searchedType = serializer.ReferenceModelType;
                    while (searchedType != typeof(object))
                    {
                        if (repositoryPropertiesByModelType.TryGetValue(searchedType, out var repositoryProperties))
                        {
                            var compatibleProperties = repositoryProperties
                                .Where(pair => pair.keyType == serializer.ReferenceKeyType)
                                .ToArray();

                            if (compatibleProperties.Length == 1)
                            {
                                var repositoryProperty = compatibleProperties[0].property;
                                serializer.SourceRepositorySelector =
                                    dbContext => (IRepository)repositoryProperty.GetValue(dbContext)!;
                            }
                            else if (violatedModelTypes.Add(serializer.ReferenceModelType))
                            {
                                violations.Add(compatibleProperties.Length > 1 ?
                                    $"model type {serializer.ReferenceModelType.Name} is handled by repositories {string.Join(", ", compatibleProperties.Select(pair => pair.property.Name))}" :
                                    $"model type {serializer.ReferenceModelType.Name} is handled by repositories with incompatible key types: {string.Join(", ", repositoryProperties.Select(pair => pair.property.Name))}");
                            }
                            break;
                        }
                        searchedType = searchedType.BaseType!;
                    }
                }
            }

            if (violations.Count > 0)
                throw new MongodmAmbiguousRepositoryException(
                    $"DbContext {dbContextEngine.DbContextType.Name} has reference serializers with ambiguous implicit source: " +
                    $"{string.Join("; ", violations)}. Set sourceRepository on the reference serializers to identify the sources");
        }

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

        /* Repositories, identity map and references address documents through the typed
         * entity id contract, while serialization addresses them through the mapped id
         * member: they must be the same member, or the two identities would diverge
         * silently. Verify that every mapped id member of an entity model type, reference
         * configuration maps included, is the implicit implementation of the contract. */
        private void ValidateIdMemberMaps()
        {
            foreach (var idMemberMap in _memberMapsById.Values.Where(mm => mm.IsIdMember))
            {
                var modelType = idMemberMap.ModelMapSchema.ModelMap.ModelType;
                if (modelType.IsInterface)
                    continue;

                var entityInterface = modelType.GetInterfaces()
                    .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntityModel<>));
                if (entityInterface is null) //no entity id contract to honor
                    continue;

                var interfaceMapping = modelType.GetInterfaceMap(entityInterface);
                var interfaceIdGetter = entityInterface.GetProperty(nameof(IEntityModel<object>.Id))!.GetMethod!;
                var contractIdGetter = interfaceMapping.TargetMethods[
                    Array.IndexOf(interfaceMapping.InterfaceMethods, interfaceIdGetter)];

                var mappedIdGetter = (idMemberMap.BsonMemberMap.MemberInfo as PropertyInfo)?.GetMethod;
                if (mappedIdGetter is null ||
                    mappedIdGetter.GetBaseDefinition() != contractIdGetter.GetBaseDefinition())
                    throw new MongodmInvalidIdMemberException(
                        $"Model map schema {idMemberMap.ModelMapSchema.Id} of type {modelType.Name} maps " +
                        $"{idMemberMap.BsonMemberMap.MemberInfo.Name} as its document id, but the id member of " +
                        $"an entity model must be the implicit implementation of " +
                        $"{nameof(IEntityModel<object>)}<TKey>.{nameof(IEntityModel<object>.Id)}");
            }
        }

        /* Model map schema ids identify on documents the schema shaping them, and must be
         * unique across the whole db context, not only inside their model map: reusing an
         * id on different model types is a source of misunderstandings. The fallback
         * schema id is a sentinel shared by all fallback schemas, and is reserved to them. */
        private void ValidateSchemaIds()
        {
            Dictionary<string, List<IModelMapSchema>> schemasById = [];
            foreach (var modelMap in _maps.Values.OfType<IModelMap>())
            {
                foreach (var schema in modelMap.SecondarySchemas.Prepend(modelMap.ActiveSchema))
                {
                    if (!schemasById.TryGetValue(schema.Id, out var schemas))
                    {
                        schemas = [];
                        schemasById.Add(schema.Id, schemas);
                    }
                    schemas.Add(schema);
                }
            }

            List<string> violations = [];
            foreach (var (id, schemas) in schemasById)
            {
                if (id == ModelMapSchema.FallbackId)
                    violations.Add($"schema id \"{id}\" is reserved to fallback schemas, and is used by model types {string.Join(", ", schemas.Select(s => s.ModelMap.ModelType.Name))}");
                else if (schemas.Count > 1)
                    violations.Add($"schema id \"{id}\" is used by model types {string.Join(", ", schemas.Select(s => s.ModelMap.ModelType.Name))}");
            }

            if (violations.Count > 0)
                throw new MongodmDuplicateSchemaIdException(
                    $"DbContext {dbContextEngine.DbContextType.Name} has model map schemas violating id uniqueness: " +
                    string.Join("; ", violations));
        }
    }
}
