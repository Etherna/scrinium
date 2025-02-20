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
using Etherna.MongODM.Core.Extensions;
using Etherna.MongODM.Core.Utility;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core.Serialization.Mapping
{
    public abstract class ModelMapSchema : FreezableConfig, IModelMapSchema
    {
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
            ArgumentNullException.ThrowIfNull(bsonClassMap, nameof(bsonClassMap));
            ArgumentNullException.ThrowIfNull(modelMap, nameof(modelMap));
            
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
                ArgumentNullException.ThrowIfNull(baseModelMapSchema, nameof(baseModelMapSchema));

                BaseSchemaId = baseModelMapSchema.Id;
                bsonClassMap.SetBaseClassMap(((ModelMapSchema)baseModelMapSchema).bsonClassMap);
            });

        public BsonMemberMap? TryGetMemberMap(string memberName) =>
            bsonClassMap.GetMemberMap(memberName);

        public void UseProxyGenerator(IDbContext dbContext) =>
            ExecuteConfigAction(() =>
            {
                ArgumentNullException.ThrowIfNull(dbContext, nameof(dbContext));
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
                bsonClassMap.SetCreator(() => dbContext.ProxyGenerator.CreateInstance(ModelMap.ModelType, dbContext));
            });

        public bool TryUseProxyGenerator(IDbContext dbContext)
        {
            ArgumentNullException.ThrowIfNull(dbContext, nameof(dbContext));

            // Verify if can use proxy model.
            if (ModelMap.ModelType != typeof(object) &&
                !ModelMap.ModelType.IsAbstract &&
                !dbContext.ProxyGenerator.IsProxyType(ModelMap.ModelType))
            {
                UseProxyGenerator(dbContext);
                return true;
            }

            return false;
        }

        // Protected methods.
        protected abstract Task<object> FixDeserializedModelHelperAsync(object model);

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
        private readonly Func<TModel, Task<TModel>>? fixDeserializedModelFunc;

        // Constructors.
        internal ModelMapSchema(
            string id,
            BsonClassMap<TModel>? bsonClassMap,
            string? baseSchemaId,
            Func<TModel, Task<TModel>>? fixDeserializedModelFunc,
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
            ArgumentNullException.ThrowIfNull(model, nameof(model));

            return fixDeserializedModelFunc is not null ?
                (await fixDeserializedModelFunc((TModel)model).ConfigureAwait(false))! :
                model;
        }
    }

    public class ModelMapSchema<TModel, TOverrideNominal> : ModelMapSchema, IModelMapSchema<TModel>
        where TOverrideNominal : class, TModel
    {
        private readonly Func<TOverrideNominal, Task<TOverrideNominal>>? fixDeserializedModelFunc;

        // Constructors.
        internal ModelMapSchema(
            string id,
            BsonClassMap<TOverrideNominal>? bsonClassMap,
            string? baseSchemaId,
            Func<TOverrideNominal, Task<TOverrideNominal>>? fixDeserializedModelFunc,
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
            ArgumentNullException.ThrowIfNull(model, nameof(model));

            return fixDeserializedModelFunc is not null ?
                (await fixDeserializedModelFunc((TOverrideNominal)model).ConfigureAwait(false))! :
                model;
        }
    }
}
