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

using Etherna.MongoDB.Bson.Serialization;
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Extensions;
using Etherna.Scrinium.Core.Utility;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Etherna.Scrinium.Core.Serialization.Mapping
{
    public abstract class ModelMapSchema : FreezableConfig, IModelMapSchema
    {
        // Consts.
        /// <summary>
        /// Previous name of the document element carrying a schema id, still recognized
        /// reading the documents written with it. Writes always use
        /// <see cref="IdElementName"/>: a document read through the deprecated name migrates
        /// to the current one with its next whole document write.
        /// </summary>
        public const string DeprecatedIdElementName = "_m";

        /* Sentinel id shared by all fallback schemas: it doesn't identify a schema
         * version on documents, and is reserved to them. */
        public const string FallbackId = "fallback";

        /// <summary>
        /// Name of the document element carrying the id of the schema that wrote the document.
        /// </summary>
        public const string IdElementName = "_s";

        // Fields.
        private readonly List<IMemberMap> _generatedMemberMaps = new();
        private readonly BsonClassMap bsonClassMap;

        // Constructors.
        protected internal ModelMapSchema(
            string id,
            string? baseSchemaId,
            BsonClassMap bsonClassMap,
            IModelMap modelMap)
        {
            ArgumentNullException.ThrowIfNull(bsonClassMap);
            ArgumentNullException.ThrowIfNull(modelMap);
            
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException($"'{nameof(id)}' cannot be null or empty", nameof(id));
            if (!modelMap.ModelType.IsAssignableFrom(bsonClassMap.ClassType))
                throw new ArgumentException($"'{nameof(bsonClassMap)}'.ClassType must be {modelMap.ModelType.Name} or derivated, instead it is {bsonClassMap.ClassType.Name}");

            Id = id;
            BaseSchemaId = baseSchemaId;
            this.bsonClassMap = bsonClassMap;
            ModelMap = modelMap ?? throw new ArgumentNullException(nameof(modelMap));
        }

        // Properties.
        public string Id { get; }
        public ReadOnlyCollection<BsonMemberMap> AllMemberMaps => bsonClassMap.AllMemberMaps;
        public IModelMapSchema? BaseSchema { get; private set; }
        public string? BaseSchemaId { get; private set; }
        public string Discriminator => bsonClassMap.Discriminator;
        public bool DiscriminatorIsRequired => bsonClassMap.DiscriminatorIsRequired;
        public BsonMemberMap? ExtraElementsMemberMap => bsonClassMap.ExtraElementsMemberMap;
        public IEnumerable<IMemberMap> GeneratedMemberMaps => _generatedMemberMaps;
        public bool HasRootClass => bsonClassMap.HasRootClass;
        public IMemberMap? IdMemberMap => GeneratedMemberMaps.FirstOrDefault(mm => mm.IsIdMember);
        public bool IsCurrentActive => ModelMap.ActiveSchema == this;
        public bool IsEntity => bsonClassMap.IsEntity();
        public bool IsRootClass => bsonClassMap.IsRootClass;
        public IModelMap ModelMap { get; }
        public Type ModelType => bsonClassMap.ClassType;
        public IBsonSerializer Serializer => bsonClassMap.ToSerializer();

        // Methods.
        public Task<object> FixDeserializedModelAsync(object model) =>
            FixDeserializedModelHelperAsync(model);

        public void SetBaseModelMapSchema(IModelMapSchema baseModelMapSchema) =>
            ExecuteConfigAction(() =>
            {
                ArgumentNullException.ThrowIfNull(baseModelMapSchema);

                BaseSchemaId = baseModelMapSchema.Id;
                bsonClassMap.SetBaseClassMap(((ModelMapSchema)baseModelMapSchema).bsonClassMap);
            });

        public BsonMemberMap? TryGetMemberMap(string memberName) =>
            bsonClassMap.GetMemberMap(memberName);

        public void UseProxyGenerator(IDbContextEngine dbContextEngine) =>
            ExecuteConfigAction(() =>
            {
                ArgumentNullException.ThrowIfNull(dbContextEngine);
                if (ModelMap.ModelType.IsAbstract)
                    throw new InvalidOperationException("Can't generate proxy of an abstract model");

                // Remove CreatorMaps.
                while (bsonClassMap.CreatorMaps.Any())
                {
                    var memberInfo = bsonClassMap.CreatorMaps.First().MemberInfo;
                    switch (memberInfo)
                    {
                        case ConstructorInfo constructorInfo:
                            bsonClassMap.UnmapConstructor(constructorInfo);
                            break;
                        case MethodInfo methodInfo:
                            bsonClassMap.UnmapFactoryMethod(methodInfo);
                            break;
                        default: throw new InvalidOperationException();
                    }
                }

                // Set creator.
                bsonClassMap.SetCreator(() => dbContextEngine.ProxyGenerator.CreateInstance(ModelMap.ModelType));
            });

        public bool TryUseProxyGenerator(IDbContextEngine dbContextEngine)
        {
            ArgumentNullException.ThrowIfNull(dbContextEngine);

            // Verify if can use proxy model.
            /* Only concrete entity models deserialize as proxies: lazy loading and change
             * candidate marking only apply to them. Any other model keeps its natural
             * class map creators. */
            if (ModelMap.ModelType is { IsClass: true, IsAbstract: false } &&
                typeof(IEntityModel).IsAssignableFrom(ModelMap.ModelType))
            {
                UseProxyGenerator(dbContextEngine);
                return true;
            }

            return false;
        }

        // Protected methods.
        protected abstract Task<object> FixDeserializedModelHelperAsync(object model);

        /// <summary>
        /// Resolve the db context scope running the current operation, required to fix
        /// deserialized models. Deserializations always run inside a db context scope.
        /// </summary>
        protected IDbContext ResolveCurrentDbContext() =>
            DbExecutionContextHandler.TryGetCurrentDbContext(ModelMap.DbContextEngine.ExecutionContext)
                ?? throw new InvalidOperationException("Can't fix a deserialized model outside of a db context scope");

        protected override void FreezeAction()
        {
            // Freeze bson class map.
            bsonClassMap.Freeze();
        }

        // Internal methods.
        internal void AddGeneratedMemberMap(IMemberMap memberMap) => _generatedMemberMaps.Add(memberMap);
    }

    public class ModelMapSchema<TModel> : ModelMapSchema, IModelMapSchema<TModel>
    {
        private readonly Func<IDbContext, TModel, Task<TModel>>? fixDeserializedModelFunc;

        // Constructors.
        internal ModelMapSchema(
            string id,
            BsonClassMap<TModel>? bsonClassMap,
            string? baseSchemaId,
            Func<IDbContext, TModel, Task<TModel>>? fixDeserializedModelFunc,
            IModelMap modelMap)
            : base(id, baseSchemaId, bsonClassMap ?? new BsonClassMap<TModel>(cm => cm.AutoMap()), modelMap)
        {
            this.fixDeserializedModelFunc = fixDeserializedModelFunc;
        }

        // Methods.
        public async Task<TModel> FixDeserializedModelAsync(TModel model)
        {
            if (model is null)
                throw new ArgumentNullException(nameof(model));

            return (TModel)await FixDeserializedModelHelperAsync(model).ConfigureAwait(false);
        }

        // Protected methods.
        protected override async Task<object> FixDeserializedModelHelperAsync(
            object model)
        {
            ArgumentNullException.ThrowIfNull(model);

            if (fixDeserializedModelFunc is null)
                return model;

            return (await fixDeserializedModelFunc(
                ResolveCurrentDbContext(),
                (TModel)model).ConfigureAwait(false))!;
        }
    }

    public class ModelMapSchema<TModel, TOverrideNominal> : ModelMapSchema, IModelMapSchema<TModel>
        where TOverrideNominal : class, TModel
    {
        private readonly Func<IDbContext, TOverrideNominal, Task<TOverrideNominal>>? fixDeserializedModelFunc;

        // Constructors.
        internal ModelMapSchema(
            string id,
            BsonClassMap<TOverrideNominal>? bsonClassMap,
            string? baseSchemaId,
            Func<IDbContext, TOverrideNominal, Task<TOverrideNominal>>? fixDeserializedModelFunc,
            IModelMap modelMap)
            : base(id, baseSchemaId, bsonClassMap ?? new BsonClassMap<TOverrideNominal>(cm => cm.AutoMap()), modelMap)
        {
            this.fixDeserializedModelFunc = fixDeserializedModelFunc;
        }

        // Methods.
        public async Task<TModel> FixDeserializedModelAsync(TModel model)
        {
            if (model is null)
                throw new ArgumentNullException(nameof(model));

            return (TModel)await FixDeserializedModelHelperAsync(model).ConfigureAwait(false);
        }

        // Protected methods.
        protected override async Task<object> FixDeserializedModelHelperAsync(
            object model)
        {
            ArgumentNullException.ThrowIfNull(model);

            if (fixDeserializedModelFunc is null)
                return model;

            return (await fixDeserializedModelFunc(
                ResolveCurrentDbContext(),
                (TOverrideNominal)model).ConfigureAwait(false))!;
        }
    }
}
