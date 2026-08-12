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
using Etherna.MongoDB.Bson.Serialization.Serializers;
using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.Exceptions;
using Etherna.MongODM.Core.Extensions;
using Etherna.MongODM.Core.Models;
using Etherna.MongODM.Core.Options;
using Etherna.MongODM.Core.Serialization.Mapping;
using Etherna.MongODM.Core.Serialization.Providers;
using Etherna.MongODM.Core.Serialization.Serializers;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Etherna.MongODM.Core
{
    public class MapRegistryTest
    {
        // Internal classes.
        /* The id member is mapped by the model map of the level declaring it: the invalid
         * id typed models declare their own id, keeping each violation on its model. */
        public class BsonArrayIdModel : IEntityModel<BsonArray>
        {
            public IDictionary<string, object>? ExtraElements { get; }
            public virtual BsonArray Id { get; set; } = null!;
            public void DisposeForDelete() { }
        }
        public class BsonDocumentIdModel : IEntityModel<BsonDocument>
        {
            public IDictionary<string, object>? ExtraElements { get; }
            public virtual BsonDocument Id { get; set; } = null!;
            public void DisposeForDelete() { }
        }
        public class BsonValueIdModel : IEntityModel<BsonValue>
        {
            public IDictionary<string, object>? ExtraElements { get; }
            public virtual BsonValue Id { get; set; } = null!;
            public void DisposeForDelete() { }
        }
        public class BsonValueMemberModel
        {
            public BsonValue? Payload { get; set; }
        }
        public class ChildModel : FakeEntityModelBase<string>
        {
            public virtual string? Name { get; set; }
            public virtual ChildModel? Parent { get; set; }
        }
        public sealed class ChildModelSerializer : SerializerBase<ChildModel>
        {
            public override ChildModel Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args) =>
                new() { Id = context.Reader.ReadString() };

            public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, ChildModel value) =>
                context.Writer.WriteString(value.Id);
        }
        public class CompositeIdModel : IEntityModel<CompositeKey>
        {
            public IDictionary<string, object>? ExtraElements { get; }
            public virtual CompositeKey Id { get; set; } = null!;
            public void DisposeForDelete() { }
        }
        public sealed class CompositeKey
        {
            public string? Area { get; set; }
            public int Number { get; set; }
        }
        public class EntityChildHostModel
        {
            public ChildModel? Child { get; set; }
            public IEnumerable<ChildModel>? Children { get; set; }
        }
        /* The two areas host model types with the same simple name, standing for types
         * declared with the same name in different namespaces: their default discriminator
         * is the simple type name, so they share it. */
        public static class FirstArea
        {
            public abstract class HomonymBaseModel
            {
                public string? Code { get; set; }
            }
            public class HomonymModel
            {
                public string? Name { get; set; }
            }
        }
        public class FirstModel
        {
            public string? Name { get; set; }
        }
        public interface IKeyModel;
        public class InterfaceIdModel : IEntityModel<IKeyModel>
        {
            public IDictionary<string, object>? ExtraElements { get; }
            public virtual IKeyModel Id { get; set; } = null!;
            public void DisposeForDelete() { }
        }
        public sealed class KeyModel(string value)
        {
            public string Value { get; } = value;
        }
        public sealed class ForeignKeyModelSerializer : SerializerBase<KeyModel>
        {
            public override KeyModel Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args) =>
                new(context.Reader.ReadString());

            public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, KeyModel value) =>
                context.Writer.WriteString(value.Value);
        }
        public sealed class KeyModelSerializer : SerializerBase<KeyModel>
        {
            public override KeyModel Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args) =>
                new(context.Reader.ReadString());

            public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, KeyModel value) =>
                context.Writer.WriteString(value.Value);
        }
        public class PlainChildHostModel
        {
            public FirstModel? Child { get; set; }
        }
        public static class SecondArea
        {
            public abstract class HomonymBaseModel
            {
                public string? Code { get; set; }
            }
            public class HomonymModel
            {
                public string? Name { get; set; }
            }
        }
        public class SecondModel
        {
            public string? Name { get; set; }
        }
        public class TreeNodeModel
        {
            public IEnumerable<TreeNodeModel>? Children { get; set; }
            public string? Name { get; set; }
            public TreeNodeModel? Parent { get; set; }
        }
        public class TwinChildHostModel
        {
            public FirstModel? FirstChild { get; set; }
            public FirstModel? SecondChild { get; set; }
        }
        public class UntypedIdModel : IEntityModel<object>
        {
            public IDictionary<string, object>? ExtraElements { get; }
            public virtual object Id { get; set; } = null!;
            public void DisposeForDelete() { }
        }
        public class WrongIdModel : FakeEntityModelBase<string>
        {
            public virtual string? Code { get; set; }
        }

        // Fields.
        private readonly Mock<IDbContextEngine> dbContextEngineMock = new();
        private readonly MapRegistry mapRegistry = new();
        private readonly BsonSerializerRegistry serializerRegistry = new();

        // Constructor.
        public MapRegistryTest()
        {
            dbContextEngineMock.Setup(e => e.DbContextType)
                .Returns(typeof(FakeDbContext));
            dbContextEngineMock.Setup(e => e.DiscriminatorRegistry)
                .Returns(new Mock<IDiscriminatorRegistry>().Object);
            dbContextEngineMock.Setup(e => e.Options.DbName)
                .Returns("fakeDb");
            dbContextEngineMock.Setup(e => e.Options.ModelMapSchemaId)
                .Returns(new ModelMapSchemaIdOptions());
            dbContextEngineMock.Setup(e => e.SerializerRegistry)
                .Returns(serializerRegistry);

            mapRegistry.Initialize(dbContextEngineMock.Object, new Mock<ILogger>().Object);
        }

        // Tests.
        [Fact]
        public void ActiveSchemasCreateInstancesWithProxyGeneratorOnlyForEntityModels()
        {
            /* MODM-189: only entity model schemas replace their creators with the proxy
             * generator; any other model keeps its natural class map creators. */

            // Setup.
            var proxyInstance = new FakeModel();
            dbContextEngineMock.Setup(e => e.ProxyGenerator.CreateInstance(typeof(FakeModel), It.IsAny<object[]>()))
                .Returns(proxyInstance);

            var entityModelMap = (IModelMap)mapRegistry.AddModelMap<FakeModel>("fakeSchemaId", ScalarMembersInitializer);
            var otherModelMap = (IModelMap)mapRegistry.AddModelMap<FirstModel>("firstSchemaId");
            mapRegistry.Freeze();

            // Action.
            var deserializedEntityModel = DeserializeModel<FakeModel>(entityModelMap.ActiveSchema.Serializer, new BsonDocument());
            var deserializedOtherModel = DeserializeModel<FirstModel>(otherModelMap.ActiveSchema.Serializer, new BsonDocument());

            // Assert.
            Assert.Same(proxyInstance, deserializedEntityModel);
            Assert.IsType<FirstModel>(deserializedOtherModel);
            dbContextEngineMock.Verify(
                e => e.ProxyGenerator.CreateInstance(typeof(FirstModel), It.IsAny<object[]>()),
                Times.Never());
        }

        [Fact]
        public void AddCustomSerializerMapClaimsSerializerRegistrySlot()
        {
            /* MODM-176: claiming the slot at registration makes later lookups resolve the
             * custom serializer also for types otherwise served by the driver serialization
             * providers (e.g. Guid, resolved as entity id type by the driver id generator
             * convention at automap). */

            // Setup.
            var customSerializer = new KeyModelSerializer();

            // Action.
            mapRegistry.AddCustomSerializerMap<KeyModel>(customSerializer);

            // Assert.
            Assert.Same(customSerializer, serializerRegistry.GetSerializer<KeyModel>());
        }

        [Fact]
        public void AddCustomSerializerMapFailsOverForeignRegisteredSerializer()
        {
            /* Only the adapter fabricated by the serialization provider, or an equal
             * serializer, are accepted as already registered (driver serializer equality
             * is type and configuration based): any other serializer registered for the
             * type is a real conflict, surfacing at the map registration claiming the
             * slot. */

            // Setup.
            serializerRegistry.RegisterSerializer(typeof(KeyModel), new ForeignKeyModelSerializer());

            // Action.
            var exception = Assert.Throws<BsonSerializationException>(() =>
                mapRegistry.AddCustomSerializerMap<KeyModel>(new KeyModelSerializer()));

            // Assert.
            Assert.Contains("already a different serializer registered", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void FreezeAcceptsFabricatedSerializerCachedBeforeMapsRegistration()
        {
            /* MODM-176: a serializer lookup executed while maps are still registering
             * (e.g. the driver id generator convention, resolving the id member serializer
             * of an entity model at auto map) caches the serializer fabricated by the
             * serialization provider. The freeze keeps it as the registered serializer:
             * it delegates every operation to the mapped custom serializer. */

            // Setup.
            dbContextEngineMock.Setup(e => e.MapRegistry)
                .Returns(mapRegistry);
            serializerRegistry.RegisterSerializationProvider(new MapRegistrySerializationProvider(dbContextEngineMock.Object));

            //the premature lookup caches the fabricated serializer
            var fabricatedSerializer = serializerRegistry.GetSerializer<KeyModel>();
            mapRegistry.AddCustomSerializerMap<KeyModel>(new KeyModelSerializer());

            // Action.
            mapRegistry.Freeze();

            // Assert.
            Assert.IsType<MappedSerializerAdapter<KeyModel>>(fabricatedSerializer);
            Assert.Same(fabricatedSerializer, serializerRegistry.GetSerializer<KeyModel>());

            //the registered serializer delegates to the mapped custom serializer
            var serializedDocument = new BsonDocument();
            using var bsonWriter = new BsonDocumentWriter(serializedDocument);
            bsonWriter.WriteStartDocument();
            bsonWriter.WriteName("key");
            fabricatedSerializer.Serialize(
                BsonSerializationContext.CreateRoot(bsonWriter),
                new BsonSerializationArgs { NominalType = typeof(KeyModel) },
                new KeyModel("keyVal"));
            bsonWriter.WriteEndDocument();
            Assert.Equal("keyVal", serializedDocument["key"].AsString);
        }

        [Fact]
        public void FreezeBuildsChildMemberMapsForRepeatedEmbeddedModelMembers()
        {
            /* MODM-224: the member maps walk skips only the schemas already open on its own
             * recursion path: a schema reached again on a sibling branch builds its child
             * member maps once per branch. */

            // Setup.
            dbContextEngineMock.Setup(e => e.MapRegistry)
                .Returns(mapRegistry);

            mapRegistry.AddModelMap<FirstModel>("firstSchemaId");
            mapRegistry.AddModelMap<TwinChildHostModel>("twinHostSchemaId", cm =>
            {
                cm.SetMemberSerializer(m => m.FirstChild!, new MappedSerializerAdapter<FirstModel>(dbContextEngineMock.Object));
                cm.SetMemberSerializer(m => m.SecondChild!, new MappedSerializerAdapter<FirstModel>(dbContextEngineMock.Object));
            });

            // Action.
            mapRegistry.Freeze();

            // Assert.
            string[] expectedMemberMapIds =
            [
                $"{nameof(TwinChildHostModel)};twinHostSchemaId;{nameof(TwinChildHostModel.FirstChild)}",
                $"{nameof(TwinChildHostModel)};twinHostSchemaId;{nameof(TwinChildHostModel.FirstChild)}|{nameof(FirstModel)};firstSchemaId;{nameof(FirstModel.Name)}",
                $"{nameof(TwinChildHostModel)};twinHostSchemaId;{nameof(TwinChildHostModel.SecondChild)}",
                $"{nameof(TwinChildHostModel)};twinHostSchemaId;{nameof(TwinChildHostModel.SecondChild)}|{nameof(FirstModel)};firstSchemaId;{nameof(FirstModel.Name)}"
            ];
            var hostModelMap = mapRegistry.GetModelMap(typeof(TwinChildHostModel));
            Assert.Equal(
                expectedMemberMapIds,
                hostModelMap.AllDescendingMemberMaps.Select(mm => mm.Id).Order(StringComparer.Ordinal));
        }

        [Fact]
        public void FreezeDoesntCreateProxiesNorProxyMaps()
        {
            /* MODM-189: model maps register only model types: the schema discovery doesn't
             * create proxy instances, and proxy types have no maps of their own. */

            // Setup.
            mapRegistry.AddModelMap<FakeModel>("fakeSchemaId", ScalarMembersInitializer);

            // Action.
            mapRegistry.Freeze();

            // Assert.
            dbContextEngineMock.Verify(
                e => e.ProxyGenerator.CreateInstance(It.IsAny<Type>(), It.IsAny<object[]>()),
                Times.Never());
            Assert.Equal(
                new[] { typeof(FakeModel), typeof(FakeEntityModelBase<string>), typeof(ModelBase) }.OrderBy(t => t.FullName),
                mapRegistry.MapsByModelType.Keys.OrderBy(t => t.FullName));
        }

        /* MODM-222: an entity id is always a value. A composite id is addressed by no atomic
         * key, and a document valued id is the only shape MongoDB reads as an operator
         * expression instead of a value: a hostile {"$ne": null} would match an arbitrary
         * document. Every id serializer declaring a document or an array representation is
         * rejected at engine build. */
        [Fact]
        public void FreezeFailsWithArraySerializedIdMemberType()
        {
            // Setup.
            mapRegistry.AddModelMap<BsonArrayIdModel>("bsonArrayIdSchemaId");

            // Action.
            var exception = Assert.Throws<MongodmInvalidIdMemberException>(() => mapRegistry.Freeze());

            // Assert.
            Assert.Contains("bsonArrayIdSchemaId", exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(BsonArrayIdModel), exception.Message, StringComparison.Ordinal);
            Assert.Contains("must serialize to a value", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void FreezeFailsWithBsonDocumentIdMemberType()
        {
            // Setup.
            mapRegistry.AddModelMap<BsonDocumentIdModel>("bsonDocumentIdSchemaId");

            // Action.
            var exception = Assert.Throws<MongodmInvalidIdMemberException>(() => mapRegistry.Freeze());

            // Assert.
            Assert.Contains("bsonDocumentIdSchemaId", exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(BsonDocumentIdModel), exception.Message, StringComparison.Ordinal);
            Assert.Contains("must serialize to a value", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void FreezeFailsWithBsonValueIdMemberType()
        {
            // Setup.
            mapRegistry.AddModelMap<BsonValueIdModel>("bsonValueIdSchemaId");

            // Action.
            var exception = Assert.Throws<MongodmInvalidIdMemberException>(() => mapRegistry.Freeze());

            // Assert.
            Assert.Contains("bsonValueIdSchemaId", exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(BsonValueIdModel), exception.Message, StringComparison.Ordinal);
            Assert.Contains("must serialize to a value", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void FreezeFailsWithInterfaceIdMemberType()
        {
            /* An interface typed id serializes through the driver discriminated interface
             * serializer, which declares a document representation: the id of the document
             * is the discriminated implementation, not a value. */

            // Setup.
            mapRegistry.AddModelMap<InterfaceIdModel>("interfaceIdSchemaId");

            // Action.
            var exception = Assert.Throws<MongodmInvalidIdMemberException>(() => mapRegistry.Freeze());

            // Assert.
            Assert.Contains("interfaceIdSchemaId", exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(InterfaceIdModel), exception.Message, StringComparison.Ordinal);
            Assert.Contains("must serialize to a value", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void FreezeFailsWithUntypedIdMemberType()
        {
            /* MODM-222: an object typed id doesn't commit to an id type. The values with
             * no BSON type equivalent discriminate into a document, and the ones with it
             * read back as the type of their BSON type — an enum id writes 1 and reads
             * back an Int32 — while the typed entity id contract, the identity map keys
             * and the references resolution all rely on the id value type. */

            // Setup.
            mapRegistry.AddModelMap<UntypedIdModel>("untypedIdSchemaId");

            // Action.
            var exception = Assert.Throws<MongodmInvalidIdMemberException>(() => mapRegistry.Freeze());

            // Assert.
            Assert.Contains("untypedIdSchemaId", exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(UntypedIdModel), exception.Message, StringComparison.Ordinal);
            Assert.Contains("doesn't commit to an id type", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void FreezeFailsWithClassMappedIdMemberType()
        {
            // Setup.
            mapRegistry.AddModelMap<CompositeIdModel>("compositeIdSchemaId");

            // Action.
            var exception = Assert.Throws<MongodmInvalidIdMemberException>(() => mapRegistry.Freeze());

            // Assert.
            Assert.Contains("compositeIdSchemaId", exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(CompositeIdModel), exception.Message, StringComparison.Ordinal);
            Assert.Contains("must serialize to a value", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void FreezeFailsWithDiscriminatorSharedByConcreteAndAbstractModelTypes()
        {
            /* MODM-238: a concrete model type writes its discriminator into its documents,
             * and any other model type declaring the same value stays a candidate at read,
             * abstract ones included. */

            // Setup.
            mapRegistry.AddModelMap<FirstArea.HomonymBaseModel>("firstBase");
            mapRegistry.AddModelMap<FirstModel>("first", cm =>
            {
                cm.AutoMap();
                cm.SetDiscriminator(nameof(FirstArea.HomonymBaseModel));
            });

            // Action.
            var exception = Assert.Throws<MongodmDuplicateDiscriminatorException>(() => mapRegistry.Freeze());

            // Assert.
            Assert.Contains($"\"{nameof(FirstArea.HomonymBaseModel)}\"", exception.Message, StringComparison.Ordinal);
            Assert.Contains(typeof(FirstArea.HomonymBaseModel).FullName!, exception.Message, StringComparison.Ordinal);
            Assert.Contains(typeof(FirstModel).FullName!, exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void FreezeFailsWithDiscriminatorSharedByModelTypes()
        {
            /* MODM-238: discriminators default to the simple type name, so two model types
             * with the same name in different namespaces write the same value into their
             * documents. Reads of a member whose nominal type both types satisfy (any
             * object shaped member) would resolve an ambiguous model type: the collision
             * fails the engine build, naming both types and the way out. */

            // Setup.
            mapRegistry.AddModelMap<FirstArea.HomonymModel>("firstHomonym");
            mapRegistry.AddModelMap<SecondArea.HomonymModel>("secondHomonym");

            // Action.
            var exception = Assert.Throws<MongodmDuplicateDiscriminatorException>(() => mapRegistry.Freeze());

            // Assert.
            Assert.Contains($"\"{nameof(FirstArea.HomonymModel)}\"", exception.Message, StringComparison.Ordinal);
            Assert.Contains(typeof(FirstArea.HomonymModel).FullName!, exception.Message, StringComparison.Ordinal);
            Assert.Contains(typeof(SecondArea.HomonymModel).FullName!, exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(BsonClassMap.SetDiscriminator), exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void FreezeFailsWithDuplicateActiveAndSecondarySchemaIdsAcrossModelMaps()
        {
            // Setup.
            mapRegistry.AddModelMap<FirstModel>("first")
                .AddSecondarySchema("shared");
            mapRegistry.AddModelMap<SecondModel>("shared");

            // Action.
            var exception = Assert.Throws<MongodmDuplicateSchemaIdException>(() => mapRegistry.Freeze());

            // Assert.
            Assert.Contains("shared", exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(FirstModel), exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(SecondModel), exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void FreezeFailsWithDuplicateActiveSchemaIdsAcrossModelMaps()
        {
            // Setup.
            mapRegistry.AddModelMap<FirstModel>("v1");
            mapRegistry.AddModelMap<SecondModel>("v1");

            // Action.
            var exception = Assert.Throws<MongodmDuplicateSchemaIdException>(() => mapRegistry.Freeze());

            // Assert.
            Assert.Contains("v1", exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(FirstModel), exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(SecondModel), exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void FreezeFailsWithDuplicateSchemaIdsInSameModelMap()
        {
            // Setup.
            mapRegistry.AddModelMap<FirstModel>("v1")
                .AddSecondarySchema("v1");

            // Action.
            var exception = Assert.Throws<MongodmDuplicateSchemaIdException>(() => mapRegistry.Freeze());

            // Assert.
            Assert.Contains("v1", exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(FirstModel), exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void FreezeFailsWithEmbeddedEntityModelMemberInReferenceConfiguration()
        {
            /* A reference can denormalize members of its model, but a denormalized entity
             * member is still a reference on its own: embedding it fails like on a root
             * model map schema. */

            // Setup.
            dbContextEngineMock.Setup(e => e.MapRegistry)
                .Returns(mapRegistry);

            mapRegistry.AddModelMap<EntityChildHostModel>("hostSchemaId", cm =>
                cm.SetMemberSerializer(m => m.Child!, new ReferenceSerializer<ChildModel, string>(
                    dbContextEngineMock.Object,
                    config => config.AddModelMap<ChildModel>("childSchemaId"))));

            // Action.
            var exception = Assert.Throws<MongodmEmbeddedEntityModelException>(() => mapRegistry.Freeze());

            // Assert.
            Assert.Contains("childSchemaId", exception.Message, StringComparison.Ordinal);
            Assert.Contains($"member {nameof(ChildModel.Parent)} of", exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(ChildModel), exception.Message, StringComparison.Ordinal);
            Assert.Contains("reference serializer", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void FreezeFailsWithEmbeddedEntityModelMembers()
        {
            /* Entity models are always referenced by other documents: a member serializing
             * one as a full embedded document, directly or into a collection, is a
             * configuration error failing the freeze with every violation detailed. */

            // Setup.
            mapRegistry.AddModelMap<EntityChildHostModel>("hostSchemaId");

            // Action.
            var exception = Assert.Throws<MongodmEmbeddedEntityModelException>(() => mapRegistry.Freeze());

            // Assert.
            Assert.Contains("hostSchemaId", exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(EntityChildHostModel), exception.Message, StringComparison.Ordinal);
            Assert.Contains($"member {nameof(EntityChildHostModel.Child)} of", exception.Message, StringComparison.Ordinal);
            Assert.Contains($"member {nameof(EntityChildHostModel.Children)} of", exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(ChildModel), exception.Message, StringComparison.Ordinal);
            Assert.Contains("reference serializer", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void FreezeFailsWithEntityModelMemberResolvingModelMapSerializer()
        {
            /* An entity model member serializer resolved through the registry embeds the
             * full document when the type is mapped with a model map: the same
             * configuration error of a direct class map serializer. */

            // Setup.
            dbContextEngineMock.Setup(e => e.MapRegistry)
                .Returns(mapRegistry);

            mapRegistry.AddModelMap<ChildModel>("childSchemaId", cm => cm.MapMember(c => c.Name));
            mapRegistry.AddModelMap<EntityChildHostModel>("hostSchemaId", cm =>
                cm.SetMemberSerializer(m => m.Child!, new MappedSerializerAdapter<ChildModel>(dbContextEngineMock.Object)));

            // Action.
            var exception = Assert.Throws<MongodmEmbeddedEntityModelException>(() => mapRegistry.Freeze());

            // Assert.
            Assert.Contains($"member {nameof(EntityChildHostModel.Child)} of", exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(ChildModel), exception.Message, StringComparison.Ordinal);
            Assert.Contains("reference serializer", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void FreezeFailsWithIdMemberNotImplementingTheEntityIdContract()
        {
            /* The typed entity id contract and the mapped id member must be the same
             * member: mapping another property as the document id would silently split
             * the persisted identity from the one addressed by the framework. */

            // Setup.
            mapRegistry.AddModelMap<WrongIdModel>("wrongId", mm =>
            {
                mm.AutoMap();
                mm.MapIdMember(m => m.Code);
            });

            // Action.
            var exception = Assert.Throws<MongodmInvalidIdMemberException>(() => mapRegistry.Freeze());

            // Assert.
            Assert.Contains(nameof(WrongIdModel), exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(WrongIdModel.Code), exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void FreezeFailsWithReservedFallbackSchemaId()
        {
            // Setup.
            mapRegistry.AddModelMap<FirstModel>("v1")
                .AddSecondarySchema(ModelMapSchema.FallbackId);

            // Action.
            var exception = Assert.Throws<MongodmDuplicateSchemaIdException>(() => mapRegistry.Freeze());

            // Assert.
            Assert.Contains(ModelMapSchema.FallbackId, exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(FirstModel), exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void FreezeKeepsCustomSerializerMapForObjectType()
        {
            /* An application hosting its own types into object shaped members can register
             * an ObjectSerializer with an explicit allow list through a custom serializer
             * map: the freeze keeps it as the registered serializer for object. */

            // Setup.
            var customObjectSerializer = new ObjectSerializer(type =>
                ObjectSerializer.DefaultAllowedTypes(type) || type == typeof(FirstModel));
            mapRegistry.AddCustomSerializerMap<object>(customObjectSerializer);
            mapRegistry.AddModelMap<FakeModel>("fakeSchemaId", ScalarMembersInitializer);

            // Action.
            mapRegistry.Freeze();

            // Assert.
            Assert.Same(customObjectSerializer, serializerRegistry.GetSerializer<object>());
        }

        [Fact]
        public void FreezeKeepsDriverObjectSerializerForObjectType()
        {
            /* MODM-231: linking base model maps stops before typeof(object), so no model
             * map serializer registers for object in place of the driver ObjectSerializer,
             * whose allowed types guard protects object shaped members. */

            // Setup.
            /* Mirror the engine registry consumption order: the driver primitive provider,
             * serving object, is consumed before the map registry provider. */
            dbContextEngineMock.Setup(e => e.MapRegistry)
                .Returns(mapRegistry);
            serializerRegistry.RegisterSerializationProvider(new MapRegistrySerializationProvider(dbContextEngineMock.Object));
            serializerRegistry.RegisterSerializationProvider(new PrimitiveSerializationProvider());

            mapRegistry.AddModelMap<FakeModel>("fakeSchemaId", ScalarMembersInitializer);

            // Action.
            mapRegistry.Freeze();

            // Assert.
            Assert.DoesNotContain(typeof(object), mapRegistry.MapsByModelType.Keys);
            Assert.IsType<ObjectSerializer>(serializerRegistry.GetSerializer<object>());
        }

        [Fact]
        public void FreezeSucceedsWithAbstractModelTypesSharingDiscriminator()
        {
            /* MODM-238: an abstract model type is never the concrete type of a serialized
             * instance, so its discriminator is never written into a document, nor looked
             * up: base model types with the same simple name keep their defaults. */

            // Setup.
            mapRegistry.AddModelMap<FirstArea.HomonymBaseModel>("firstBase");
            mapRegistry.AddModelMap<SecondArea.HomonymBaseModel>("secondBase");

            // Action.
            mapRegistry.Freeze();

            // Assert.
            Assert.True(mapRegistry.IsFrozen);
            Assert.Equal(
                mapRegistry.GetModelMap(typeof(FirstArea.HomonymBaseModel)).ActiveSchema.Discriminator,
                mapRegistry.GetModelMap(typeof(SecondArea.HomonymBaseModel)).ActiveSchema.Discriminator);
        }

        [Fact]
        public void FreezeSucceedsWithBsonValueMember()
        {
            /* The driver BsonValue serializer reports itself as its own array item
             * serializer: the freeze serializer explorations terminate on the cycle, and
             * a non id BsonValue member stays valid. */

            // Setup.
            mapRegistry.AddModelMap<BsonValueMemberModel>("bsonValueMemberSchemaId");

            // Action.
            mapRegistry.Freeze();

            // Assert.
            Assert.True(mapRegistry.IsFrozen);
        }

        [Fact]
        public void FreezeSucceedsWithCustomSerializedEntityModelMember()
        {
            /* A custom serializer set on an entity model member never enters the document
             * serialization pipeline: an explicit opt out for value-object-like models. */

            // Setup.
            mapRegistry.AddModelMap<EntityChildHostModel>("hostSchemaId", cm =>
                cm.SetMemberSerializer(m => m.Child!, new ChildModelSerializer()));

            // Action.
            mapRegistry.Freeze();

            // Assert.
            Assert.True(mapRegistry.IsFrozen);
        }

        [Fact]
        public void FreezeSucceedsWithCyclicReferenceSummarySchemas()
        {
            /* MODM-224: a reference summary schema can reach itself through the reference
             * members it denormalizes: the member maps walk stops when a schema repeats on
             * its recursion path, instead of overflowing the stack at engine build. */

            // Setup.
            dbContextEngineMock.Setup(e => e.MapRegistry)
                .Returns(mapRegistry);

            BsonClassMap<ChildModel> childSummaryClassMap = null!;
            var childRefSerializer = new ReferenceSerializer<ChildModel, string>(
                dbContextEngineMock.Object,
                config => config.AddModelMap<ChildModel>("childRefSchemaId", cm =>
                {
                    childSummaryClassMap = cm;
                    cm.MapMember(c => c.Name);
                }));
            //close the cycle: the summary schema references its model through the same serializer
            childSummaryClassMap.MapMember(c => c.Parent).SetSerializer(childRefSerializer);

            mapRegistry.AddModelMap<EntityChildHostModel>("hostSchemaId", cm =>
                cm.SetMemberSerializer(m => m.Child!, childRefSerializer));

            // Action.
            mapRegistry.Freeze();

            // Assert.
            string[] expectedMemberMapIds =
            [
                $"{nameof(EntityChildHostModel)};hostSchemaId;{nameof(EntityChildHostModel.Child)}",
                $"{nameof(EntityChildHostModel)};hostSchemaId;{nameof(EntityChildHostModel.Child)}|{nameof(ChildModel)};childRefSchemaId;{nameof(ChildModel.Name)}",
                $"{nameof(EntityChildHostModel)};hostSchemaId;{nameof(EntityChildHostModel.Child)}|{nameof(ChildModel)};childRefSchemaId;{nameof(ChildModel.Parent)}"
            ];
            var hostModelMap = mapRegistry.GetModelMap(typeof(EntityChildHostModel));
            Assert.Equal(
                expectedMemberMapIds,
                hostModelMap.AllDescendingMemberMaps.Select(mm => mm.Id).Order(StringComparer.Ordinal));
        }

        [Fact]
        public void FreezeSucceedsWithEmbeddedPlainModelMember()
        {
            /* Only entity models can't embed: models without identity keep serializing
             * as embedded documents. */

            // Setup.
            mapRegistry.AddModelMap<PlainChildHostModel>("plainHostSchemaId");

            // Action.
            mapRegistry.Freeze();

            // Assert.
            Assert.True(mapRegistry.IsFrozen);
        }

        [Fact]
        public void FreezeSucceedsWithEntityModelMemberResolvingCustomSerializer()
        {
            /* An entity model type mapped with a custom serializer map keeps its custom
             * serialization also when the member serializer resolves through the
             * registry. */

            // Setup.
            dbContextEngineMock.Setup(e => e.MapRegistry)
                .Returns(mapRegistry);

            mapRegistry.AddCustomSerializerMap<ChildModel>(new ChildModelSerializer());
            mapRegistry.AddModelMap<EntityChildHostModel>("hostSchemaId", cm =>
                cm.SetMemberSerializer(m => m.Child!, new MappedSerializerAdapter<ChildModel>(dbContextEngineMock.Object)));

            // Action.
            mapRegistry.Freeze();

            // Assert.
            Assert.True(mapRegistry.IsFrozen);
        }

        [Fact]
        public void FreezeSucceedsWithExplicitDiscriminatorsOnHomonymModelTypes()
        {
            /* MODM-238: model types with the same simple name coexist by declaring
             * distinct discriminators, the way out of the default collision. */

            // Setup.
            mapRegistry.AddModelMap<FirstArea.HomonymModel>("firstHomonym", cm =>
            {
                cm.AutoMap();
                cm.SetDiscriminator("firstHomonymModel");
            });
            mapRegistry.AddModelMap<SecondArea.HomonymModel>("secondHomonym", cm =>
            {
                cm.AutoMap();
                cm.SetDiscriminator("secondHomonymModel");
            });

            // Action.
            mapRegistry.Freeze();

            // Assert.
            Assert.True(mapRegistry.IsFrozen);
        }

        [Fact]
        public void FreezeSucceedsWithFallbackSchemasOnDifferentModelMaps()
        {
            // Setup.
            mapRegistry.AddModelMap<FirstModel>("first")
                .AddFallbackSchema();
            mapRegistry.AddModelMap<SecondModel>("second")
                .AddFallbackSchema();

            // Action.
            mapRegistry.Freeze();

            // Assert.
            Assert.True(mapRegistry.IsFrozen);
        }

        [Fact]
        public void FreezeSucceedsWithReferencedEntityModelMembers()
        {
            /* Reference serializers are the valid way to serialize entity model members,
             * directly or into a collection. */

            // Setup.
            dbContextEngineMock.Setup(e => e.MapRegistry)
                .Returns(mapRegistry);

            mapRegistry.AddModelMap<EntityChildHostModel>("hostSchemaId", cm =>
            {
                cm.SetMemberSerializer(m => m.Child!, new ReferenceSerializer<ChildModel, string>(
                    dbContextEngineMock.Object,
                    config => config.AddModelMap<ChildModel>("childSchemaId", cm2 => cm2.MapMember(c => c.Name))));
                cm.SetMemberSerializer(m => m.Children!, new EnumerableSerializer<ChildModel>(
                    new ReferenceSerializer<ChildModel, string>(
                        dbContextEngineMock.Object,
                        config => config.AddModelMap<ChildModel>("otherChildSchemaId", _ => { }))));
            });

            // Action.
            mapRegistry.Freeze();

            // Assert.
            Assert.True(mapRegistry.IsFrozen);
        }

        [Fact]
        public void FreezeSucceedsWithSelfNestingModelMembers()
        {
            /* MODM-224: a mapped type reachable from itself through its member serializers
             * is a legal non entity configuration: the member maps walk stops when a schema
             * repeats on its recursion path, building each member map path once instead of
             * overflowing the stack at engine build. */

            // Setup.
            dbContextEngineMock.Setup(e => e.MapRegistry)
                .Returns(mapRegistry);

            mapRegistry.AddModelMap<TreeNodeModel>("treeNodeSchemaId", cm =>
            {
                cm.MapMember(m => m.Name);
                cm.SetMemberSerializer(m => m.Children!, new EnumerableSerializer<TreeNodeModel>(
                    new MappedSerializerAdapter<TreeNodeModel>(dbContextEngineMock.Object)));
                cm.SetMemberSerializer(m => m.Parent!, new MappedSerializerAdapter<TreeNodeModel>(dbContextEngineMock.Object));
            });

            // Action.
            mapRegistry.Freeze();

            // Assert.
            string[] expectedMemberMapIds =
            [
                $"{nameof(TreeNodeModel)};treeNodeSchemaId;{nameof(TreeNodeModel.Children)}",
                $"{nameof(TreeNodeModel)};treeNodeSchemaId;{nameof(TreeNodeModel.Name)}",
                $"{nameof(TreeNodeModel)};treeNodeSchemaId;{nameof(TreeNodeModel.Parent)}"
            ];
            var treeNodeModelMap = mapRegistry.GetModelMap(typeof(TreeNodeModel));
            Assert.Equal(
                expectedMemberMapIds,
                treeNodeModelMap.AllDescendingMemberMaps.Select(mm => mm.Id).Order(StringComparer.Ordinal));
        }

        [Fact]
        public void FreezeSucceedsWithUniqueSchemaIds()
        {
            // Setup.
            mapRegistry.AddModelMap<FirstModel>("first")
                .AddSecondarySchema("first-old");
            mapRegistry.AddModelMap<SecondModel>("second")
                .AddSecondarySchema("second-old");

            // Action.
            mapRegistry.Freeze();

            // Assert.
            Assert.True(mapRegistry.IsFrozen);
            Assert.Equal("first", mapRegistry.GetActiveSchemaIdBsonElement(typeof(FirstModel)).Value.AsString);
            Assert.Equal("second", mapRegistry.GetActiveSchemaIdBsonElement(typeof(SecondModel)).Value.AsString);
        }

        [Fact]
        public void FreezeSucceedsWithUntypedIdMemberMappingItsSerializer()
        {
            /* An application serializing its own id values through a custom serializer map
             * for object declares how they serialize and deserialize: the id type commits
             * to a representation, and the rendered shape stays verified by the id filters
             * and by the create write. */

            // Setup.
            mapRegistry.AddCustomSerializerMap(new ObjectSerializer(
                type => ObjectSerializer.DefaultAllowedTypes(type) || type == typeof(FirstModel)));
            mapRegistry.AddModelMap<UntypedIdModel>("untypedIdSchemaId");

            // Action.
            mapRegistry.Freeze();

            // Assert.
            Assert.True(mapRegistry.IsFrozen);
        }

        // Helpers.
        private static TModel DeserializeModel<TModel>(IBsonSerializer serializer, BsonDocument document)
        {
            var bsonReader = new BsonDocumentReader(document);
            return (TModel)serializer.Deserialize(
                BsonDeserializationContext.CreateRoot(bsonReader),
                new BsonDeserializationArgs { NominalType = typeof(TModel) });
        }

        /* FakeModel has entity model typed members, which can't serialize embedded:
         * map only the scalar members. */
        private static void ScalarMembersInitializer(BsonClassMap<FakeModel> classMap)
        {
            classMap.AutoMap();
            classMap.UnmapMember(m => m.EnumerableProp);
            classMap.UnmapMember(m => m.ObjectProp);
        }
    }
}
