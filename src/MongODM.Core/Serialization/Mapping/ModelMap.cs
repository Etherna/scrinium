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
using Etherna.MongODM.Core.Serialization.Serializers;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core.Serialization.Mapping
{
    public abstract class ModelMap : MapBase, IModelMap
    {
        // Fields.
        private IModelMapSchema _activeSchema = null!;
        private readonly List<IMemberMap> _definedMemberMaps = new();
        private Dictionary<string, IModelMapSchema> _schemasById = null!;
#pragma warning disable CA1051
#pragma warning disable CA1002
        protected readonly List<IModelMapSchema> _secondarySchemas = new();
#pragma warning restore CA1002
#pragma warning restore CA1051

        // Constructor.
        protected ModelMap(
            IDbContext dbContext,
            Type modelType)
            : base(modelType)
        {
            ArgumentNullException.ThrowIfNull(modelType, nameof(modelType));
            
            DbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

            // Verify if uses proxy model.
            if (modelType.IsClass &&
                modelType != typeof(object) &&
                !modelType.IsAbstract &&
                !dbContext.ProxyGenerator.IsProxyType(modelType))
            {
                ProxyModelType = dbContext.ProxyGenerator.CreateInstance(modelType, dbContext).GetType();
            }
        }

        // Properties.
        public IModelMapSchema ActiveSchema
        {
            get => _activeSchema;
            internal set
            {
                _activeSchema = value;
                _activeSchema.TryUseProxyGenerator(DbContext);
            }
        }
        public override IBsonSerializer ActiveSerializer => ActiveSchema.Serializer;
        public IEnumerable<IMemberMap> AllDescendingMemberMaps => DefinedMemberMaps.Concat(
                                                                  DefinedMemberMaps.SelectMany(mm => mm.AllDescendingMemberMaps));
        public IDbContext DbContext { get; }
        public IEnumerable<IMemberMap> DefinedMemberMaps
        {
            get
            {
                Freeze(); //needed for initialization
                return _definedMemberMaps;
            }
        }
        public IModelMapSchema? FallbackSchema { get; protected set; }
        public IBsonSerializer? FallbackSerializer { get; protected set; }
        public override Type? ProxyModelType { get; }
        public IReadOnlyDictionary<string, IModelMapSchema> SchemasById
        {
            get
            {
                if (_schemasById is null)
                {
                    var modelMaps = new[] { ActiveSchema }.Concat(_secondarySchemas);

                    if (FallbackSchema is not null)
                        modelMaps = modelMaps.Append(FallbackSchema);

                    var result = modelMaps.ToDictionary(modelMap => modelMap.Id);

                    if (!IsFrozen)
                        return result;

                    //optimize performance only if frozen
                    _schemasById = result;
                }
                return _schemasById;
            }
        }
        public IEnumerable<IModelMapSchema> SecondarySchemas => _secondarySchemas;

        // Internal methods.
        internal void InitializeMemberMaps()
        {
            foreach (var schema in SchemasById.Values)
            {
                foreach (var bsonMemberMap in schema.BsonClassMap.AllMemberMaps)
                {
                    var memberMap = BuildMemberMap(bsonMemberMap, schema, null);
                    _definedMemberMaps.Add(memberMap);
                    ((ModelMapSchema)schema).AddGeneratedMemberMap(memberMap);
                }
            }
        }

        // Protected methods.
        protected void AddFallbackCustomSerializerHelper(IBsonSerializer fallbackSerializer) =>
            ExecuteConfigAction(() =>
            {
                ArgumentNullException.ThrowIfNull(fallbackSerializer, nameof(fallbackSerializer));
                if (FallbackSerializer is not null)
                    throw new InvalidOperationException("Fallback serializer already setted");

                FallbackSerializer = fallbackSerializer;
            });

        protected void AddFallbackModelMapSchemaHelper(IModelMapSchema fallbackSchema) =>
            ExecuteConfigAction(() =>
            {
                ArgumentNullException.ThrowIfNull(fallbackSchema, nameof(fallbackSchema));
                if (FallbackSchema is not null)
                    throw new InvalidOperationException("Fallback model map schema already setted");

                FallbackSchema = fallbackSchema;
            });

        protected void AddSecondarySchemaHelper(IModelMapSchema schema) =>
            ExecuteConfigAction(() =>
            {
                ArgumentNullException.ThrowIfNull(schema, nameof(schema));

                // Try to use proxy model generator.
                schema.TryUseProxyGenerator(DbContext);

                // Add schema.
                _secondarySchemas.Add(schema);
                return this;
            });

        protected override void FreezeAction()
        {
            // Freeze schemas.
            foreach (var schema in SchemasById.Values)
                schema.Freeze();
        }

        // Helpers.
        private MemberMap BuildMemberMap(
            BsonMemberMap bsonMemberMap,
            IModelMapSchema modelMapSchema,
            IMemberMap? parentMemberMap)
        {
            var memberMap = new MemberMap(bsonMemberMap, modelMapSchema, parentMemberMap);

            // Analize recursion on member.
            var memberSerializer = bsonMemberMap.GetSerializer();
            bool iterateOnArrayItem;
            do
            {
                iterateOnArrayItem = false;

                if (memberSerializer is IModelMapsHandlingSerializer modelMapsContainerSerializer)
                {
                    foreach (var modelMap in modelMapsContainerSerializer.HandledModelMaps
                        .Where(mm => !DbContext.ProxyGenerator.IsProxyType(mm.ModelType))) //skip model maps on proxy types
                    {
                        foreach (var schema in modelMap.SchemasById.Values)
                        {
                            schema.Freeze();

                            // Recursion on child member maps.
                            foreach (var childBsonMemberMap in schema.BsonClassMap.AllMemberMaps)
                            {
                                var childMemberMap = BuildMemberMap(childBsonMemberMap, schema, memberMap);
                                memberMap.AddChildMemberMap(childMemberMap);
                                ((ModelMapSchema)schema).AddGeneratedMemberMap(childMemberMap);
                            }
                        }
                    }
                }

                //in case of array serializers not defined by mongodm (as mongo driver's default)
                else if (memberSerializer is IBsonArraySerializer bsonArraySerializer &&
                    bsonArraySerializer.TryGetItemSerializationInfo(out BsonSerializationInfo itemSerializationInfo))
                {
                    // Iterate on item serializer.
                    memberSerializer = itemSerializationInfo.Serializer;
                    iterateOnArrayItem = true;
                }
            } while (iterateOnArrayItem);

            return memberMap;
        }
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope")]
    public sealed class ModelMap<TModel>(IDbContext dbContext)
        : ModelMap(dbContext, typeof(TModel)), IModelMapBuilder<TModel>
    {
        // Methods.
        public IModelMapBuilder<TModel> AddFallbackCustomSerializer(IBsonSerializer<TModel> fallbackSerializer)
        {
            AddFallbackCustomSerializerHelper(fallbackSerializer);
            return this;
        }

        public IModelMapBuilder<TModel> AddFallbackSchema(
            Action<BsonClassMap<TModel>>? modelMapSchemaInitializer = null,
            string? baseSchemaId = null,
            Func<TModel, Task<TModel>>? fixDeserializedModelFunc = null)
        {
            AddFallbackModelMapSchemaHelper(new ModelMapSchema<TModel>(
                "fallback",
                new BsonClassMap<TModel>(modelMapSchemaInitializer ?? (cm => cm.AutoMap())),
                baseSchemaId,
                fixDeserializedModelFunc,
                this));
            return this;
        }

        public IModelMapBuilder<TModel> AddSecondarySchema(
            string id,
            Action<BsonClassMap<TModel>>? modelMapSchemaInitializer = null,
            string? baseSchemaId = null,
            Func<TModel, Task<TModel>>? fixDeserializedModelFunc = null)
        {
            AddSecondarySchemaHelper(new ModelMapSchema<TModel>(
                id,
                new BsonClassMap<TModel>(modelMapSchemaInitializer ?? (cm => cm.AutoMap())),
                baseSchemaId,
                fixDeserializedModelFunc,
                this));
            return this;
        }

        public IModelMapBuilder<TModel> AddSecondarySchema<TOverrideNominal>(
            string id,
            Action<BsonClassMap<TOverrideNominal>>? modelMapSchemaInitializer = null,
            string? baseSchemaId = null,
            Func<TOverrideNominal, Task<TOverrideNominal>>? fixDeserializedModelFunc = null)
            where TOverrideNominal : class, TModel
        {
            AddSecondarySchemaHelper(new ModelMapSchema<TModel, TOverrideNominal>(
                id,
                new BsonClassMap<TOverrideNominal>(modelMapSchemaInitializer ?? (cm => cm.AutoMap())),
                baseSchemaId,
                fixDeserializedModelFunc,
                this));
            return this;
        }
    }
}
