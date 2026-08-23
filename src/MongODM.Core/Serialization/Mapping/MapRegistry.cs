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
using Etherna.MongODM.Core.Exceptions;
using Etherna.MongODM.Core.Extensions;
using Etherna.MongODM.Core.Options;
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.Core.Serialization.Serializers;
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
        private readonly List<(Type ModelType, Type KeyType, Func<IDbContext, IRepository> Selector, Type? DbContextType)> declaredSourceReferences = new();
        private readonly List<IReferenceSerializer> implicitSourceReferences = new();
        private readonly Dictionary<Type, IMap> _maps = new(); //model type -> map
        private readonly Dictionary<string, IMemberMap> _memberMapsById = new();

        private readonly Dictionary<Type, BsonElement> activeSchemaIdBsonElement = new();
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

                // Claim the serializer registry slot.
                /* Claiming the slot now makes later lookups resolve the custom serializer
                 * also for types otherwise served by the driver serialization providers
                 * (e.g. Guid, resolved as entity id type by the driver id generator
                 * convention at automap). */
                RegisterMappedSerializer(typeof(TModel), customSerializer);

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
        internal void AddDeclaredSourceReference(Type modelType, Type keyType, Func<IDbContext, IRepository> selector, Type? dbContextType)
        {
            lock (declaredSourceReferences)
                declaredSourceReferences.Add((modelType, keyType, selector, dbContextType));
        }

        internal void AddImplicitSourceReference(IReferenceSerializer serializer)
        {
            lock (implicitSourceReferences)
                implicitSourceReferences.Add(serializer);
        }

        /* Reference serializers declaring a source repository must declare a compatible
         * one: hosting the reference model type, with the same key type. Selectors need a
         * db context instance to access its repository properties: the engine builder
         * provides its own, validating at initialization, before any use.
         * A typed declaration on a db context type not implemented by the builder is a
         * cross db context source: its selector can't run on the builder, but the typed
         * factory already guarantees the repository compatibility at compile time, so
         * here only its declared db context type is validated, as reachable through one
         * single child db context type declared by the options. */
        internal void ValidateDeclaredSourceReferences(IDbContext dbContext)
        {
            var violations = new List<string>();
            var crossDbContextViolations = new List<string>();
            lock (declaredSourceReferences)
            {
                foreach (var (modelType, keyType, selector, dbContextType) in declaredSourceReferences)
                {
                    if (dbContextType is not null && !dbContextType.IsInstanceOfType(dbContext))
                    {
                        var childDbContextTypes = dbContextEngine.Options.ChildDbContextTypes
                            .Where(dbContextType.IsAssignableFrom)
                            .ToArray();
                        if (childDbContextTypes.Length == 0)
                            crossDbContextViolations.Add(
                                $"reference serializer of model type {modelType.Name} declares its source repository on db context type {dbContextType.Name}, " +
                                $"neither implemented by this db context nor declared as its child db context type " +
                                $"(declare it with {nameof(DbContextOptions)}.{nameof(DbContextOptions.ParentFor)})");
                        else if (childDbContextTypes.Length > 1)
                            crossDbContextViolations.Add(
                                $"reference serializer of model type {modelType.Name} declares its source repository on db context type {dbContextType.Name}, " +
                                $"implemented by multiple child db context types: {string.Join(", ", childDbContextTypes.Select(t => t.Name))}");
                        continue;
                    }

                    var repository = selector(dbContext);
                    if (!repository.ModelType.IsAssignableFrom(modelType))
                        violations.Add($"reference serializer of model type {modelType.Name} declares source repository {repository.Name}, handling the incompatible model type {repository.ModelType.Name}");
                    else if (repository.KeyType != keyType)
                        violations.Add($"reference serializer of model type {modelType.Name} with key type {keyType.Name} declares source repository {repository.Name}, handling the incompatible key type {repository.KeyType.Name}");
                }
            }

            if (crossDbContextViolations.Count > 0)
                throw new InvalidOperationException(
                    $"DbContext {dbContextEngine.DbContextType.Name} has reference serializers declaring unreachable source db contexts: " +
                    string.Join("; ", crossDbContextViolations));

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

            // Verify that discriminators identify a single model type.
            ValidateDiscriminators();

            // Freeze, register serializers and compile registers.
            foreach (var map in _maps.Values)
            {
                // Freeze model map.
                map.Freeze();

                // Register active serializer.
                RegisterMappedSerializer(map.ModelType, map.Serializer);

                // Register discriminators for all bson class maps.
                if (map is IModelMap modelMap)
                    foreach (var modelMapSchema in modelMap.SchemasById.Values)
                        dbContextEngine.DiscriminatorRegistry.AddDiscriminator(modelMapSchema.ModelType, modelMapSchema.Discriminator);
            }

            // Verify that entity model members serialize as references.
            ValidateEntityModelMembers();

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

                // Generate active schema id bson elements.
                activeSchemaIdBsonElement.Add(
                    modelMap.ModelType,
                    new BsonElement(
                        ModelMapSchema.IdElementName,
                        new BsonString(modelMap.ActiveSchema.Id)));
            }

            // Verify that mapped id members implement the entity id contract.
            ValidateIdMemberMaps();

            // Report the reference element paths the dependencies propagation can't address.
            ReportNotPropagatedReferencePaths();
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

        /* Class map serializers write the full document of their model: the driver ones
         * directly, the model map serializer through its schemas. */
        private static bool IsClassMapSerializer(IBsonSerializer serializer)
        {
            for (var type = serializer.GetType(); type is not null; type = type.BaseType)
            {
                if (!type.IsGenericType)
                    continue;

                var typeDefinition = type.GetGenericTypeDefinition();
                if (typeDefinition == typeof(BsonClassMapSerializer<>) ||
                    typeDefinition == typeof(ModelMapSerializer<>))
                    return true;
            }
            return false;
        }

        /* Reference serializers without a declared source repository deduce it from their
         * model and key types: resolve them at engine build to the single compatible
         * db context repository property, failing fast with the involved repositories
         * detail when the deduction is ambiguous. References without any compatible
         * repository fail fast too: a reference to a model of another db context must
         * declare its source with the typed factory. Every reference of a built engine
         * binds a source repository.
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
            var ambiguousViolations = new List<string>();
            var unresolvableViolations = new List<string>();
            var violatedModelTypes = new HashSet<Type>();
            lock (implicitSourceReferences)
            {
                foreach (var serializer in implicitSourceReferences)
                {
                    var foundRepositoryLevel = false;
                    var searchedType = serializer.ReferenceModelType;
                    while (searchedType != typeof(object))
                    {
                        if (repositoryPropertiesByModelType.TryGetValue(searchedType, out var repositoryProperties))
                        {
                            foundRepositoryLevel = true;

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
                                ambiguousViolations.Add(compatibleProperties.Length > 1 ?
                                    $"model type {serializer.ReferenceModelType.Name} is handled by repositories {string.Join(", ", compatibleProperties.Select(pair => pair.property.Name))}" :
                                    $"model type {serializer.ReferenceModelType.Name} is handled by repositories with incompatible key types: {string.Join(", ", repositoryProperties.Select(pair => pair.property.Name))}");
                            }
                            break;
                        }
                        searchedType = searchedType.BaseType!;
                    }

                    if (!foundRepositoryLevel && violatedModelTypes.Add(serializer.ReferenceModelType))
                        unresolvableViolations.Add($"model type {serializer.ReferenceModelType.Name} has no compatible repository on this db context");
                }
            }

            if (ambiguousViolations.Count > 0)
                throw new MongodmAmbiguousRepositoryException(
                    $"DbContext {dbContextEngine.DbContextType.Name} has reference serializers with ambiguous implicit source: " +
                    $"{string.Join("; ", ambiguousViolations)}. Set sourceRepository on the reference serializers to identify the sources");

            if (unresolvableViolations.Count > 0)
                throw new InvalidOperationException(
                    $"DbContext {dbContextEngine.DbContextType.Name} has reference serializers without any resolvable source repository: " +
                    $"{string.Join("; ", unresolvableViolations)}. Add a compatible repository, or declare a cross db context source " +
                    $"with the typed {nameof(ReferenceSerializer)}.{nameof(ReferenceSerializer.Create)} factory");
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

                    // Skip if the model type is at the top of its hierarchy.
                    /* typeof(object) must not get a map: its model map serializer would
                     * register in place of the driver ObjectSerializer, dropping the
                     * allowed types guard on object shaped members, and breaking the
                     * driver serializers requiring an ObjectSerializer registered for
                     * object (e.g. on interface typed members). The class map freeze
                     * resolves the object base class map on its own. */
                    if (baseModelType is null || baseModelType == typeof(object))
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

        /* Serializer lookups can run while maps are still registering (e.g. the driver id
         * generator convention, resolving the id member serializer of an entity model at
         * automap), caching serializers before the maps freeze: the adapter fabricated by
         * the serialization provider delegates to the serializer mapped here, so it can
         * stay as the registered one. A serializer already registered by a previous claim
         * of the same map is accepted as is; any other conflict is a real error. */
        private void RegisterMappedSerializer(Type modelType, IBsonSerializer serializer)
        {
            var serializerRegistry = (BsonSerializerRegistry)dbContextEngine.SerializerRegistry;
            try
            {
                serializerRegistry.TryRegisterSerializer(modelType, serializer);
            }
            catch (BsonSerializationException)
            {
                if (serializerRegistry.GetSerializer(modelType).GetType() !=
                    typeof(MappedSerializerAdapter<>).MakeGenericType(modelType))
                    throw;
            }
        }

        /* The dependencies propagation addresses the referencing documents server side,
         * filtering on the reference id element path: a path containing an unknown
         * document key — a dictionary in document representation — can't render a filter
         * (querying unknown document keys is unsupported, see upstream SERVER-267), so the
         * update propagation skips it and its summaries go stale when the referenced
         * models change, the origin delete propagation leaves its references untouched,
         * and the missing origin references scan reports it unverifiable. The
         * configuration stays legitimate: report every such path at engine build, one per
         * hosting model type and element path whatever the schemas producing it, so the
         * limitation is a conscious choice instead of a silent behavior. The reaction is
         * declared by DbContextOptions.NotPropagatedReferences: a warning per path (the
         * default), silent tolerance, or a detailed exception denying the build. */
        private void ReportNotPropagatedReferencePaths()
        {
            if (dbContextEngine.Options.NotPropagatedReferences == ReactionMode.Silent)
                return;

            HashSet<(Type ModelType, string ElementPath)> notPropagatedPaths = [];
            foreach (var idMemberMap in _memberMapsById.Values.Where(mm =>
                mm is { IsEntityReferenceMember: true, IsIdMember: true, ElementPathHasUndefinedDocumentElement: true }))
            {
                notPropagatedPaths.Add((
                    idMemberMap.MemberMapPath.First().ModelMapSchema.ModelMap.ModelType,
                    idMemberMap.ParentMemberMap!.RenderElementPath(
                        referToFinalItem: false,
                        _ => "",
                        _ => ".*")));
            }

            var orderedPaths = notPropagatedPaths
                .OrderBy(path => path.ModelType.Name, StringComparer.Ordinal)
                .ThenBy(path => path.ElementPath, StringComparer.Ordinal);

            if (dbContextEngine.Options.NotPropagatedReferences == ReactionMode.Throw &&
                notPropagatedPaths.Count > 0)
                throw new MongodmNotPropagatedReferenceException(
                    $"DbContext {dbContextEngine.DbContextType.Name} maps references behind unknown document keys, " +
                    "at element paths the dependencies propagation can't address: " +
                    string.Join("; ", orderedPaths.Select(path => $"{path.ElementPath} of model type {path.ModelType.Name}")) +
                    ". Their summaries go stale when the referenced models change, the origin delete propagation " +
                    "leaves their references untouched, and the missing origin references scan can't verify them. " +
                    "Serialize the dictionaries with an addressable representation (like ArrayOfDocuments), or " +
                    "configure DbContextOptions.NotPropagatedReferences to tolerate them");

            foreach (var (modelType, elementPath) in orderedPaths)
                logger.MapRegistryFoundNotPropagatedReferencePath(
                    dbContextEngine.Options.DbName,
                    elementPath,
                    modelType);
        }

        /* A document carries the discriminator of the concrete model type that wrote it,
         * and reads resolve the type back from it: a discriminator declared by more than
         * one model type resolves multiple candidates, and the read fails as ambiguous as
         * soon as the nominal type of the deserializing member is satisfied by more than
         * one of them (any object shaped member is). Discriminators default to the simple
         * type name, so two model types with the same name in different namespaces collide
         * on it: verify that every discriminator identifies a single model type.
         * Only concrete types write their discriminator into documents: an abstract type
         * is never the concrete type of a serialized instance, so a discriminator shared
         * by abstract types alone is never written, nor looked up, and stays valid (an
         * application base class homonym of a library one is a common configuration). */
        private void ValidateDiscriminators()
        {
            Dictionary<string, HashSet<Type>> modelTypesByDiscriminator = [];
            foreach (var modelMap in _maps.Values.OfType<IModelMap>())
            {
                foreach (var schema in modelMap.SchemasById.Values)
                {
                    if (!modelTypesByDiscriminator.TryGetValue(schema.Discriminator, out var modelTypes))
                    {
                        modelTypes = [];
                        modelTypesByDiscriminator.Add(schema.Discriminator, modelTypes);
                    }
                    modelTypes.Add(schema.ModelType);
                }
            }

            List<string> violations = [];
            foreach (var (discriminator, modelTypes) in modelTypesByDiscriminator)
                if (modelTypes.Count > 1 && modelTypes.Any(modelType => !modelType.IsAbstract))
                    violations.Add($"discriminator \"{discriminator}\" is used by model types {string.Join(", ", modelTypes.Select(modelType => modelType.FullName))}");

            if (violations.Count > 0)
                throw new MongodmDuplicateDiscriminatorException(
                    $"DbContext {dbContextEngine.DbContextType.Name} has model types sharing a document discriminator: " +
                    string.Join("; ", violations) +
                    ". Documents written with a shared discriminator can't resolve their model type at read: " +
                    "set a distinct discriminator on the colliding model map schemas with " +
                    $"{nameof(BsonClassMap)}.{nameof(BsonClassMap.SetDiscriminator)}");
        }

        /* Entity models are always referenced by other documents: serializing one as a
         * full embedded document is unsupported, since lazy loading, saving and identity
         * of an embedded entity would be undefined. Verify that no member serializes an
         * entity model type through its class map, reference configuration maps
         * included: references are the supported serialization. A custom serializer, set
         * on the member or mapped for the type, never enters the document serialization
         * pipeline, and opts out for value-object-like models.
         * The exploration runs on the schema class maps, before member maps
         * initialization: an invalid configuration fails with its detailed violations
         * before any member map is built and registered. */
        private void ValidateEntityModelMembers()
        {
            List<string> violations = [];
            HashSet<IModelMap> checkedModelMaps = [];
            var processingModelMaps = new Stack<IModelMap>(_maps.Values.OfType<IModelMap>());

            while (processingModelMaps.Count > 0)
            {
                var modelMap = processingModelMaps.Pop();
                if (!checkedModelMaps.Add(modelMap))
                    continue;

                foreach (var schema in modelMap.SchemasById.Values)
                {
                    foreach (var bsonMemberMap in schema.AllMemberMaps)
                    {
                        var serializer = bsonMemberMap.GetSerializer();
                        HashSet<IBsonSerializer> visitedSerializers = [];
                        while (true)
                        {
                            //terminate on serializer cycles (e.g. the driver BsonValue serializer descends to itself)
                            if (!visitedSerializers.Add(serializer))
                                break;

                            //unwrap the adapter binding a derived member type to its entity serializer
                            if (serializer is IEntityModelSerializerAdapter serializerAdapter)
                            {
                                serializer = serializerAdapter.SerializerBase;
                                continue;
                            }

                            //reference members are valid: check their configuration maps too
                            if (serializer is IReferenceSerializer referenceSerializer)
                            {
                                foreach (var referencedModelMap in referenceSerializer.Configuration.ModelMaps.Values)
                                    processingModelMaps.Push(referencedModelMap);
                                break;
                            }

                            //resolve the serializer mapped for the type: a model map serializer, or a custom one
                            if (serializer.GetType() is { IsGenericType: true } serializerType &&
                                serializerType.GetGenericTypeDefinition() == typeof(MappedSerializerAdapter<>))
                            {
                                if (!TryGetMappedSerializer(serializer.ValueType, out var mappedSerializer))
                                    break; //missing maps are reported by the member maps initialization
                                serializer = mappedSerializer;
                                continue;
                            }

                            //class map serializers embed the full document of an entity model
                            if (IsClassMapSerializer(serializer) &&
                                typeof(IEntityModel).IsAssignableFrom(serializer.ValueType))
                            {
                                violations.Add(
                                    $"member {bsonMemberMap.MemberName} of model map schema \"{schema.Id}\" " +
                                    $"of type {schema.ModelType.Name} embeds entity model type {serializer.ValueType.Name}");
                                break;
                            }

                            //descend containers to their serialized items
                            /* Some serializers implement the container interfaces also when they
                             * are not able to provide the required information: try with the
                             * dictionary value first, then with the array item. */
                            if (serializer is IBsonDictionarySerializer dictionarySerializer)
                            {
                                try
                                {
                                    serializer = dictionarySerializer.ValueSerializer;
                                    continue;
                                }
                                catch { }
                            }
                            if (serializer is IBsonArraySerializer arraySerializer &&
                                arraySerializer.TryGetItemSerializationInfo(out var itemSerializationInfo))
                            {
                                serializer = itemSerializationInfo.Serializer;
                                continue;
                            }

                            break;
                        }
                    }
                }
            }

            if (violations.Count > 0)
                throw new MongodmEmbeddedEntityModelException(
                    $"DbContext {dbContextEngine.DbContextType.Name} has members serializing entity models as embedded documents: " +
                    string.Join("; ", violations) +
                    ". Entity models can only be referenced by other documents: serialize these members with a " +
                    "reference serializer, or with a custom serializer for value-object-like models");
        }

        /* Repositories, identity map and references address documents through the typed
         * entity id contract, while serialization addresses them through the mapped id
         * member: they must be the same member, or the two identities would diverge
         * silently. Verify that every mapped id member of an entity model type, reference
         * configuration maps included, is the implicit implementation of the contract.
         * An entity id is also always a value, never a composite: repositories address a
         * document by an atomic key, and a document valued id is the only shape MongoDB
         * can read as an operator expression instead of a value (a filter value document
         * whose first element name starts with "$"), so a caller sending the id
         * {"$ne": null} would match, delete or overwrite an arbitrary document. Verify
         * that no id member serializer declares a document or array representation: it
         * rejects composite ids (class mapped, dictionary, interface, BsonDocument and
         * BsonValue members) at engine build. The shapes a serializer doesn't declare — a
         * custom serializer emitting a document — are refused when they render, by the id
         * filters and by the create write.
         * An id also commits to its type, which an object typed id doesn't: the driver
         * object serializer writes the values with a BSON type equivalent as plain values
         * and discriminates any other one into a document, and a value reads back as the
         * type of its BSON type — an enum id writes 1 and reads back an Int32 — while the
         * typed entity id contract, the identity map keys and the references resolution
         * all rely on the id value type. Verify that no id member is typed object, unless
         * the application maps its own serializer for it, declaring how its values
         * serialize and deserialize. */
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

                if (idMemberMap.BsonMemberMap.GetSerializer() is IBsonDocumentSerializer or IBsonArraySerializer)
                    throw new MongodmInvalidIdMemberException(
                        $"Model map schema {idMemberMap.ModelMapSchema.Id} of type {modelType.Name} maps " +
                        $"{idMemberMap.BsonMemberMap.MemberInfo.Name} of type " +
                        $"{idMemberMap.BsonMemberMap.MemberType.Name} as its document id, but an entity id must " +
                        "serialize to a value, and its serializer represents a composite. Serialize a composite id " +
                        "into a value (a string, for instance), and map its components as members of the model to " +
                        "query them");

                if (idMemberMap.BsonMemberMap.MemberType == typeof(object) && !_maps.ContainsKey(typeof(object)))
                    throw new MongodmInvalidIdMemberException(
                        $"Model map schema {idMemberMap.ModelMapSchema.Id} of type {modelType.Name} maps " +
                        $"{idMemberMap.BsonMemberMap.MemberInfo.Name} of type object as its document id, but an " +
                        "object typed id doesn't commit to an id type: the values without a BSON type equivalent " +
                        "serialize as discriminated documents, and the ones with it read back as the type of their " +
                        "BSON type (an enum id reads back an Int32). Use a concrete id type, or map a custom " +
                        "serializer for object");
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
