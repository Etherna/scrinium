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
using Etherna.MongODM.Core.Serialization.Mapping;
using Etherna.MongODM.Core.Serialization.Serializers;
using Moq;
using System;
using Xunit;

namespace Etherna.MongODM.Core
{
    public class MappedSerializerAdapterTest
    {
        // Internal classes.
        public sealed class FakeKey(string value)
        {
            public string Value { get; } = value;
        }
        public sealed class FakeKeySerializer : SerializerBase<FakeKey>
        {
            public override FakeKey Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args) =>
                new(context.Reader.ReadString());

            public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, FakeKey value) =>
                context.Writer.WriteString(value.Value);
        }

        // Fields.
        private readonly Mock<IDbContextEngine> dbContextEngineMock = new();
        private readonly Mock<IMapRegistry> mapRegistryMock = new();

        // Constructor.
        public MappedSerializerAdapterTest()
        {
            dbContextEngineMock.Setup(c => c.MapRegistry)
                .Returns(mapRegistryMock.Object);
        }

        // Tests.
        [Fact]
        public void DeserializeDelegatesToMappedSerializer()
        {
            /* MODM-176: the adapter is fabricated by the serialization provider for types
             * resolved through the serializer registry (e.g. an entity id type, resolved
             * while maps are still registering): every operation delegates to the
             * serializer mapped by the map registry. */

            // Setup.
            var document = new BsonDocument(new BsonElement("key", new BsonString("keyVal")));
            var bsonReader = new BsonDocumentReader(document);
            var adapter = new MappedSerializerAdapter<FakeKey>(dbContextEngineMock.Object);

            mapRegistryMock.Setup(r => r.GetMappedSerializer(typeof(FakeKey)))
                .Returns(new FakeKeySerializer());

            // Action.
            bsonReader.ReadStartDocument();
            bsonReader.ReadName();
            var result = adapter.Deserialize(
                BsonDeserializationContext.CreateRoot(bsonReader),
                new BsonDeserializationArgs { NominalType = typeof(FakeKey) });
            bsonReader.ReadEndDocument();

            // Assert.
            Assert.Equal("keyVal", result.Value);
        }

        [Fact]
        public void DocumentInterfacesAreNeutralWithNotImplementingMappedSerializer()
        {
            /* A mapped serializer without document oriented interfaces keeps the adapter
             * surface defined: no handled model maps, no document id, no member
             * serialization info. */

            // Setup.
            var adapter = new MappedSerializerAdapter<FakeKey>(dbContextEngineMock.Object);

            mapRegistryMock.Setup(r => r.GetMappedSerializer(typeof(FakeKey)))
                .Returns(new FakeKeySerializer());

            // Action & Assert.
            Assert.Empty(adapter.HandledModelMaps);
            Assert.False(adapter.GetDocumentId(new FakeKey("keyVal"), out _, out _, out _));
            Assert.False(adapter.TryGetMemberSerializationInfo(nameof(FakeKey.Value), out _));
        }

        [Fact]
        public void DocumentInterfacesDelegateToImplementingMappedSerializer()
        {
            // Setup.
            var adapter = new MappedSerializerAdapter<FakeKey>(dbContextEngineMock.Object);
            var handledModelMap = new Mock<IModelMap>().Object;
            var memberSerializationInfo = new BsonSerializationInfo("elem", new FakeKeySerializer(), typeof(FakeKey));

            object idOut = "idVal";
            Type idNominalTypeOut = typeof(string);
            IIdGenerator idGeneratorOut = null!;
            var mappedSerializerMock = new Mock<IBsonSerializer>();
            mappedSerializerMock.As<IBsonIdProvider>()
                .Setup(s => s.GetDocumentId(It.IsAny<object>(), out idOut, out idNominalTypeOut, out idGeneratorOut))
                .Returns(true);
            mappedSerializerMock.As<IBsonDocumentSerializer>()
                .Setup(s => s.TryGetMemberSerializationInfo("member", out memberSerializationInfo))
                .Returns(true);
            mappedSerializerMock.As<IModelMapsHandlingSerializer>()
                .Setup(s => s.HandledModelMaps)
                .Returns([handledModelMap]);
            mapRegistryMock.Setup(r => r.GetMappedSerializer(typeof(FakeKey)))
                .Returns(mappedSerializerMock.Object);

            var model = new FakeKey("keyVal");

            // Action & Assert.
            Assert.Same(handledModelMap, Assert.Single(adapter.HandledModelMaps));
            Assert.True(adapter.GetDocumentId(model, out var id, out _, out _));
            Assert.Equal("idVal", id);
            Assert.True(adapter.TryGetMemberSerializationInfo("member", out var serializationInfo));
            Assert.Equal("elem", serializationInfo.ElementName);

            adapter.SetDocumentId(model, "newId");
            mappedSerializerMock.As<IBsonIdProvider>()
                .Verify(s => s.SetDocumentId(model, "newId"), Times.Once());
        }

        [Fact]
        public void SerializeDelegatesToMappedSerializer()
        {
            // Setup.
            var adapter = new MappedSerializerAdapter<FakeKey>(dbContextEngineMock.Object);

            mapRegistryMock.Setup(r => r.GetMappedSerializer(typeof(FakeKey)))
                .Returns(new FakeKeySerializer());

            var serializedDocument = new BsonDocument();
            using var bsonWriter = new BsonDocumentWriter(serializedDocument);

            // Action.
            bsonWriter.WriteStartDocument();
            bsonWriter.WriteName("key");
            adapter.Serialize(
                BsonSerializationContext.CreateRoot(bsonWriter),
                new BsonSerializationArgs { NominalType = typeof(FakeKey) },
                new FakeKey("keyVal"));
            bsonWriter.WriteEndDocument();

            // Assert.
            Assert.Equal(0, serializedDocument.CompareTo(new BsonDocument(new BsonElement("key", new BsonString("keyVal")))));
        }
    }
}
