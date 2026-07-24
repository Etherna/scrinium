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
        private IBsonSerializer? _serializer;

        // Constructor.
        protected ModelMap(
            IDbContextEngine dbContextEngine,
            Type modelType)
            : base(modelType)
        {
            ArgumentNullException.ThrowIfNull(modelType);

            DbContextEngine = dbContextEngine ?? throw new ArgumentNullException(nameof(dbContextEngine));
        }

        // Properties.
        public IModelMapSchema ActiveSchema
        {
            get => _activeSchema;
            internal set
            {
                _activeSchema = value;
                _activeSchema.TryUseProxyGenerator(DbContextEngine);
            }
        }
        public IEnumerable<IMemberMap> AllDescendingMemberMaps => DefinedMemberMaps.Concat(
                                                                  DefinedMemberMaps.SelectMany(mm => mm.AllDescendingMemberMaps));
        public IDbContextEngine DbContextEngine { get; }
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
        public override IBsonSerializer Serializer
        {
            get
            {
                if (_serializer == null)
                {
                    var modelMapSerializerDefinition = typeof(ModelMapSerializer<>);
                    var modelMapSerializerType = modelMapSerializerDefinition.MakeGenericType(ModelType);
                    _serializer = (IBsonSerializer)Activator.CreateInstance(modelMapSerializerType, DbContextEngine)!;
                }
                
                return _serializer;
            }
        }

        // Internal methods.
        internal void InitializeMemberMaps()
        {
            foreach (var schema in SchemasById.Values)
            {
                foreach (var bsonMemberMap in schema.AllMemberMaps)
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
                ArgumentNullException.ThrowIfNull(fallbackSerializer);
                if (FallbackSerializer is not null)
                    throw new InvalidOperationException("Fallback serializer already setted");

                FallbackSerializer = fallbackSerializer;
            });

        protected void AddFallbackModelMapSchemaHelper(IModelMapSchema fallbackSchema) =>
            ExecuteConfigAction(() =>
            {
                ArgumentNullException.ThrowIfNull(fallbackSchema);
                if (FallbackSchema is not null)
                    throw new InvalidOperationException("Fallback model map schema already setted");

                FallbackSchema = fallbackSchema;
            });

        protected void AddSecondarySchemaHelper(IModelMapSchema schema) =>
            ExecuteConfigAction(() =>
            {
                ArgumentNullException.ThrowIfNull(schema);

                // Try to use proxy model generator.
                schema.TryUseProxyGenerator(DbContextEngine);

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
        private static MemberMap BuildMemberMap(
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
                    foreach (var modelMap in modelMapsContainerSerializer.HandledModelMaps)
                    {
                        foreach (var schema in modelMap.SchemasById.Values)
                        {
                            schema.Freeze();

                            // Recursion on child member maps.
                            foreach (var childBsonMemberMap in schema.AllMemberMaps)
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
    public sealed class ModelMap<TModel>(IDbContextEngine dbContextEngine)
        : ModelMap(dbContextEngine, typeof(TModel)), IModelMapBuilder<TModel>
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
            Func<IDbContext, TModel, Task<TModel>>? fixDeserializedModelFunc = null)
        {
            AddFallbackModelMapSchemaHelper(new ModelMapSchema<TModel>(
                ModelMapSchema.FallbackId,
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
            Func<IDbContext, TModel, Task<TModel>>? fixDeserializedModelFunc = null)
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
            Func<IDbContext, TOverrideNominal, Task<TOverrideNominal>>? fixDeserializedModelFunc = null)
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
