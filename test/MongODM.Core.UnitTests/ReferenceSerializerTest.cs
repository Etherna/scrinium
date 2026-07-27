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
using Etherna.MongODM.Core.Conventions;
using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.ExecContext.AsyncLocal;
using Etherna.MongODM.Core.Extensions;
using Etherna.MongODM.Core.Models;
using Etherna.MongODM.Core.Options;
using Etherna.MongODM.Core.ProxyModels;
using Etherna.MongODM.Core.Serialization.Mapping;
using Etherna.MongODM.Core.Serialization.Modifiers;
using Etherna.MongODM.Core.Serialization.Serializers;
using Moq;
using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;

namespace Etherna.MongODM.Core
{
    public class ReferenceSerializerTest
    {
        // Fields.
        private readonly Mock<IDbContextEngine> dbContextEngineMock = new();
        private readonly Mock<IDiscriminatorRegistry> discriminatorRegistryMock = new();
        private readonly Mock<ISerializerModifierAccessor> serializerModifierAccessorMock = new();

        // Constructor.
        public ReferenceSerializerTest()
        {
            discriminatorRegistryMock.Setup(r => r.LookupDiscriminatorConvention(It.IsAny<Type>()))
                .Returns(() => new HierarchicalProxyTolerantDiscriminatorConvention(dbContextEngineMock.Object, "_t"));

            dbContextEngineMock.Setup(e => e.DiscriminatorRegistry)
                .Returns(() => discriminatorRegistryMock.Object);
            dbContextEngineMock.Setup(e => e.ExecutionContext)
                .Returns(AsyncLocalContext.Instance);
            dbContextEngineMock.Setup(e => e.MapRegistry)
                .Returns(new MapRegistry());
            dbContextEngineMock.Setup(e => e.Options.ModelMapSchemaId)
                .Returns(new ModelMapSchemaIdOptions());
            dbContextEngineMock.Setup(e => e.ProxyGenerator.CreateInstance(It.IsAny<Type>(), It.IsAny<object[]>()))
                .Returns<Type, object[]>((type, arguments) => Activator.CreateInstance(
                    type,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    arguments,
                    null)!);
            dbContextEngineMock.Setup(e => e.ProxyGenerator.IsProxyType(It.IsAny<Type>()))
                .Returns(false);
            dbContextEngineMock.Setup(e => e.ProxyGenerator.PurgeProxyType(It.IsAny<Type>()))
                .Returns<Type>(t => t);
            dbContextEngineMock.Setup(e => e.SerializerModifierAccessor)
                .Returns(() => serializerModifierAccessorMock.Object);
        }

        // Tests.
        [Fact]
        public void DeserializeClearsExtraElementsOnLoadedSummary()
        {
            /* MODM-3: members can be removed from a reference schema without changing its id
             * (the document model tolerates added and removed fields), so a document written
             * when the member was still serialized resolves the schema, landing the unmapped
             * element in the extra elements bag. The bag is emptied after the load: extra
             * data is never needed with references. */

            // Setup.
            var serializer = BuildSerializer();
            var document = new BsonDocument
            {
                { "_s", "activeSchemaId" },
                { "_id", "idVal" },
                { "StringProp", "ok" },
                { "removedProp", "extraVal" }
            };
            var bsonReader = new BsonDocumentReader(document);

            // Action.
            var result = serializer.Deserialize(
                BsonDeserializationContext.CreateRoot(bsonReader),
                new BsonDeserializationArgs { NominalType = typeof(FakeModel) });

            // Assert.
            Assert.Equal("idVal", result.Id);
            Assert.Equal("ok", result.StringProp);
            //an instantiated empty bag proves the unmapped element landed there before the clear
            Assert.NotNull(result.ExtraElements);
            Assert.Empty(result.ExtraElements);
        }

        [Fact]
        public void DeserializeDoesNotMarkMemberAppliedByDefaultValueAsLoaded()
        {
            /* A member missing from the reference document assigns its specified default value
             * during deserialization, but carries no summary loaded data: it stays out of the
             * summary loaded member names, lazy loading the actual value from the origin
             * document at its first get. */

            // Setup.
            var serializer = BuildSerializer(mm =>
            {
                mm.AutoMap();
                mm.MapMember(m => m.IntegerProp).SetDefaultValue(42);
            });
            dbContextEngineMock.Setup(e => e.ProxyGenerator.CreateInstance(typeof(FakeModel), It.IsAny<object[]>()))
                .Returns(new FakeModelProxy());
            dbContextEngineMock.Setup(e => e.ProxyGenerator.IsProxyType(typeof(FakeModelProxy)))
                .Returns(true);

            var document = new BsonDocument
            {
                { "_s", "activeSchemaId" },
                { "_id", "idVal" },
                { "StringProp", "ok" }
            };
            var bsonReader = new BsonDocumentReader(document);

            // Action.
            var result = serializer.Deserialize(
                BsonDeserializationContext.CreateRoot(bsonReader),
                new BsonDeserializationArgs { NominalType = typeof(FakeModel) });

            // Assert.
            var referenceableResult = Assert.IsAssignableFrom<IReferenceable>(result);
            Assert.True(referenceableResult.IsSummary);
            Assert.Contains("StringProp", referenceableResult.SettedMemberNames);
            Assert.DoesNotContain("IntegerProp", referenceableResult.SettedMemberNames);
        }

        [Fact]
        public void DeserializeKeepsObservedSettedMembersWithCustomFallbackSerializer()
        {
            /* A custom fallback serializer deserializes without a schema mapping document
             * elements to members: the members observed as setted through the proxy overrides
             * stay the only available source for the summary loaded member names. */

            // Setup.
            var fallbackClassMap = new BsonClassMap<FakeModel>(cm =>
            {
                cm.AutoMap();
                cm.SetCreator(() => new FakeModelProxy());
            });
            fallbackClassMap.Freeze();

            var serializer = new ReferenceSerializer<FakeModel, string>(dbContextEngineMock.Object, config =>
            {
                config.AddModelMap<ModelBase>("modelBaseSchemaId");
                config.AddModelMap<FakeEntityModelBase<string>>("baseSchemaId", mm => mm.MapIdMember(m => m.Id));
                config.AddModelMap<FakeModel>("activeSchemaId")
                    .AddFallbackCustomSerializer((IBsonSerializer<FakeModel>)fallbackClassMap.ToSerializer());
            });
            dbContextEngineMock.Setup(e => e.ProxyGenerator.IsProxyType(typeof(FakeModelProxy)))
                .Returns(true);

            var document = new BsonDocument
            {
                { "_s", "unknownSchemaId" },
                { "_id", "idVal" },
                { "StringProp", "ok" }
            };
            var bsonReader = new BsonDocumentReader(document);

            // Action.
            var result = serializer.Deserialize(
                BsonDeserializationContext.CreateRoot(bsonReader),
                new BsonDeserializationArgs { NominalType = typeof(FakeModel) });

            // Assert.
            var referenceableResult = Assert.IsAssignableFrom<IReferenceable>(result);
            Assert.True(referenceableResult.IsSummary);
            Assert.Contains("StringProp", referenceableResult.SettedMemberNames);
            Assert.DoesNotContain("Id", referenceableResult.SettedMemberNames);
            Assert.Equal("idVal", result.Id);
        }

        [Fact]
        public void DeserializeMarksNotObservableSetterMemberAsSummaryLoaded()
        {
            /* A set through a private setter is not observable by the proxy member overrides.
             * The summary loaded member names derive from the reference document, so the
             * member reports as loaded anyway, without triggering a spurious full load at
             * its first get. */

            // Setup.
            var serializer = new ReferenceSerializer<FakeModelWithPrivateSetter, string>(dbContextEngineMock.Object, config =>
            {
                config.AddModelMap<ModelBase>("modelBaseSchemaId");
                config.AddModelMap<FakeEntityModelBase<string>>("baseSchemaId", mm => mm.MapIdMember(m => m.Id));
                config.AddModelMap<FakeModelWithPrivateSetter>("privateSetterSchemaId", mm =>
                {
                    mm.AutoMap();
                    mm.MapMember(m => m.PrivateSetterProp);
                });
            });
            dbContextEngineMock.Setup(e => e.ProxyGenerator.CreateInstance(typeof(FakeModelWithPrivateSetter), It.IsAny<object[]>()))
                .Returns(new FakeModelWithPrivateSetterProxy());
            dbContextEngineMock.Setup(e => e.ProxyGenerator.IsProxyType(typeof(FakeModelWithPrivateSetterProxy)))
                .Returns(true);

            var document = new BsonDocument
            {
                { "_s", "privateSetterSchemaId" },
                { "_id", "idVal" },
                { "ObservableProp", "observedVal" },
                { "PrivateSetterProp", "notObservedVal" }
            };
            var bsonReader = new BsonDocumentReader(document);

            // Action.
            var result = serializer.Deserialize(
                BsonDeserializationContext.CreateRoot(bsonReader),
                new BsonDeserializationArgs { NominalType = typeof(FakeModelWithPrivateSetter) });

            // Assert.
            var referenceableResult = Assert.IsAssignableFrom<IReferenceable>(result);
            Assert.True(referenceableResult.IsSummary);
            Assert.Contains("ObservableProp", referenceableResult.SettedMemberNames);
            Assert.Contains("PrivateSetterProp", referenceableResult.SettedMemberNames);
            //a get of the loaded member reads the summary value, without a full load attempt
            Assert.Equal("notObservedVal", result.PrivateSetterProp);
        }

        [Fact]
        public void DeserializeMarksProxyModelAsSummaryWithDocumentMembers()
        {
            /* A model deserialized as reference is a summary: only the members carried by the
             * summary document are loaded. The id never joins the summary member names,
             * definitionally present on any instance. */

            // Setup.
            var serializer = BuildSerializer();
            dbContextEngineMock.Setup(e => e.ProxyGenerator.CreateInstance(typeof(FakeModel), It.IsAny<object[]>()))
                .Returns(new FakeModelProxy());
            dbContextEngineMock.Setup(e => e.ProxyGenerator.IsProxyType(typeof(FakeModelProxy)))
                .Returns(true);

            var document = new BsonDocument
            {
                { "_s", "activeSchemaId" },
                { "_id", "idVal" },
                { "StringProp", "ok" }
            };
            var bsonReader = new BsonDocumentReader(document);

            // Action.
            var result = serializer.Deserialize(
                BsonDeserializationContext.CreateRoot(bsonReader),
                new BsonDeserializationArgs { NominalType = typeof(FakeModel) });

            // Assert.
            var referenceableResult = Assert.IsAssignableFrom<IReferenceable>(result);
            Assert.True(referenceableResult.IsSummary);
            Assert.Contains("StringProp", referenceableResult.SettedMemberNames);
            Assert.DoesNotContain("Id", referenceableResult.SettedMemberNames);
            Assert.Equal("idVal", result.Id);
        }

        [Fact]
        public void DeserializeReadsOnlyIdWithReadOnlyReferencedIdModifier()
        {
            // Setup.
            var serializer = BuildSerializer();
            dbContextEngineMock.Setup(e => e.ProxyGenerator.CreateInstance(typeof(FakeModel), It.IsAny<object[]>()))
                .Returns(new FakeModelProxy());
            dbContextEngineMock.Setup(e => e.ProxyGenerator.IsProxyType(typeof(FakeModelProxy)))
                .Returns(true);
            serializerModifierAccessorMock.Setup(a => a.IsReadOnlyReferencedIdEnabled)
                .Returns(true);

            var document = new BsonDocument
            {
                { "_s", "activeSchemaId" },
                { "_id", "idVal" },
                { "StringProp", "ok" }
            };
            var bsonReader = new BsonDocumentReader(document);

            // Action.
            var result = serializer.Deserialize(
                BsonDeserializationContext.CreateRoot(bsonReader),
                new BsonDeserializationArgs { NominalType = typeof(FakeModel) });

            // Assert.
            //the summary keeps no setted member: any member get reloads, only the id stays readable
            var referenceableResult = Assert.IsAssignableFrom<IReferenceable>(result);
            Assert.True(referenceableResult.IsSummary);
            Assert.Empty(referenceableResult.SettedMemberNames);
            Assert.Equal("idVal", result.Id);
        }

        [Fact]
        public void DeserializeReturnsNullWithNullValue()
        {
            // Setup.
            var serializer = BuildSerializer();
            var document = new BsonDocument(new BsonElement("elem", BsonNull.Value));
            var bsonReader = new BsonDocumentReader(document);
            bsonReader.ReadStartDocument();
            bsonReader.ReadName();

            // Action.
            var result = serializer.Deserialize(
                BsonDeserializationContext.CreateRoot(bsonReader),
                new BsonDeserializationArgs { NominalType = typeof(FakeModel) });
            bsonReader.ReadEndDocument();

            // Assert.
            Assert.Null(result);
        }

        [Fact]
        public void DeserializeReturnsNullWithProxyModelWithoutId()
        {
            /* A reference missing its id can't be resolved to its origin document: the
             * referred instance is ignored. */

            // Setup.
            var serializer = BuildSerializer();
            dbContextEngineMock.Setup(e => e.ProxyGenerator.CreateInstance(typeof(FakeModel), It.IsAny<object[]>()))
                .Returns(new FakeModelProxy());
            dbContextEngineMock.Setup(e => e.ProxyGenerator.IsProxyType(typeof(FakeModelProxy)))
                .Returns(true);

            var document = new BsonDocument
            {
                { "_s", "activeSchemaId" },
                { "StringProp", "ok" }
            };
            var bsonReader = new BsonDocumentReader(document);

            // Action.
            var result = serializer.Deserialize(
                BsonDeserializationContext.CreateRoot(bsonReader),
                new BsonDeserializationArgs { NominalType = typeof(FakeModel) });

            // Assert.
            Assert.Null(result);
        }

        [Fact]
        public void DeserializeThrowsWithNotDocumentValue()
        {
            // Setup.
            var serializer = BuildSerializer();
            var document = new BsonDocument(new BsonElement("elem", new BsonInt32(42)));
            var bsonReader = new BsonDocumentReader(document);
            bsonReader.ReadStartDocument();
            bsonReader.ReadName();

            // Action.
            var exception = Assert.Throws<InvalidOperationException>(() => serializer.Deserialize(
                BsonDeserializationContext.CreateRoot(bsonReader),
                new BsonDeserializationArgs { NominalType = typeof(FakeModel) }));

            // Assert.
            Assert.Contains("Expected a nested document", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void SerializeClearsExtraElementsBeforeWriting()
        {
            /* The persistence side protection of the extra data: a reference write drops the
             * bag content, both from the model and from the written document. */

            // Setup.
            var serializer = BuildSerializer(mm => mm.MapMember(m => m.StringProp));
            dbContextEngineMock.Setup(e => e.ProxyGenerator.PurgeProxyType(typeof(FakeModelWithExtraElements)))
                .Returns(typeof(FakeModel));

            var model = new FakeModelWithExtraElements { Id = "idVal", StringProp = "ok" };
            model.SetExtraElements(new Dictionary<string, object> { ["removedProp"] = "extraVal" });

            var serializedDocument = new BsonDocument();
            using var bsonWriter = new BsonDocumentWriter(serializedDocument);

            // Action.
            serializer.Serialize(
                BsonSerializationContext.CreateRoot(bsonWriter),
                new BsonSerializationArgs { NominalType = typeof(FakeModel) },
                model);

            // Assert.
            Assert.NotNull(model.ExtraElements);
            Assert.Empty(model.ExtraElements);
            Assert.False(serializedDocument.Contains("removedProp"));
        }

        [Fact]
        public void SerializeWritesNullWithNullModel()
        {
            // Setup.
            var serializer = BuildSerializer();
            var serializedDocument = new BsonDocument();
            using var bsonWriter = new BsonDocumentWriter(serializedDocument);
            bsonWriter.WriteStartDocument();
            bsonWriter.WriteName("elem");

            // Action.
            serializer.Serialize(
                BsonSerializationContext.CreateRoot(bsonWriter),
                new BsonSerializationArgs { NominalType = typeof(FakeModel) },
                null!);
            bsonWriter.WriteEndDocument();

            // Assert.
            Assert.Equal(0, serializedDocument.CompareTo(new BsonDocument(new BsonElement("elem", BsonNull.Value))));
        }

        [Fact]
        public void SerializeWritesSummaryDocumentWithActiveSchemaId()
        {
            /* A reference document carries only the members mapped by the active reference
             * schema, stamped with its schema id: the summary shape, not the full model. */

            // Setup.
            var serializer = BuildSerializer(mm => mm.MapMember(m => m.StringProp));
            var model = new FakeModel { Id = "idVal", IntegerProp = 42, StringProp = "ok" };
            var serializedDocument = new BsonDocument();
            using var bsonWriter = new BsonDocumentWriter(serializedDocument);

            // Action.
            serializer.Serialize(
                BsonSerializationContext.CreateRoot(bsonWriter),
                new BsonSerializationArgs { NominalType = typeof(FakeModel) },
                model);

            // Assert.
            var expectedDocument = new BsonDocument
            {
                { "_s", "activeSchemaId" },
                { "_id", "idVal" },
                { "StringProp", "ok" }
            };
            Assert.Equal(0, serializedDocument.CompareTo(expectedDocument));
        }

        // Helpers.
        private ReferenceSerializer<FakeModel, string> BuildSerializer(Action<BsonClassMap<FakeModel>>? fakeModelInitializer = null) =>
            new(dbContextEngineMock.Object, config =>
            {
                //the auto mapped ModelBase map carries the extra elements member, as in real configurations
                config.AddModelMap<ModelBase>("modelBaseSchemaId");
                config.AddModelMap<FakeEntityModelBase<string>>("baseSchemaId", mm => mm.MapIdMember(m => m.Id));
                config.AddModelMap<FakeModel>("activeSchemaId", fakeModelInitializer ?? (mm => mm.AutoMap()));
            });
    }
}
