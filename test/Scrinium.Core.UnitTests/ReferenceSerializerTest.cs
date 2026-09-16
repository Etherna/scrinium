// Copyright 2020-present Etherna SA
// This file is part of Scrinium.
//
// Scrinium is free software: you can redistribute it and/or modify it under the terms of the
// GNU Lesser General Public License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
//
// Scrinium is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY;
// without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License along with Scrinium.
// If not, see <https://www.gnu.org/licenses/>.

using Etherna.MongoDB.Bson;
using Etherna.MongoDB.Bson.IO;
using Etherna.MongoDB.Bson.Serialization;
using Etherna.Scrinium.Core.Conventions;
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Etherna.Scrinium.Core.Extensions;
using Etherna.Scrinium.Core.Models;
using Etherna.Scrinium.Core.Options;
using Etherna.Scrinium.Core.ProxyModels;
using Etherna.Scrinium.Core.Repositories;
using Etherna.Scrinium.Core.Serialization.Mapping;
using Etherna.Scrinium.Core.Serialization.Modifiers;
using Etherna.Scrinium.Core.Serialization.Serializers;
using Etherna.Scrinium.Core.Utility;
using Moq;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Xunit;

namespace Etherna.Scrinium.Core
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
            dbContextEngineMock.Setup(e => e.Options.DbName)
                .Returns("testDb");
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
            /* SCR-3: members can be removed from a reference schema without changing its id
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

        [Theory]
        [InlineData(null, ReactionMode.Warn)] //warned by default
        [InlineData(ReactionMode.Silent, ReactionMode.Silent)]
        [InlineData(ReactionMode.Throw, ReactionMode.Throw)]
        public void DeserializeStampsSummariesWithTheConfiguredReactionMode(
            ReactionMode? configuredMode,
            ReactionMode expectedMode)
        {
            /* The full load of a summary runs much later than its deserialization, on an
             * instance knowing only its source repository: the mode declared by the reference
             * travels with the summary it deserializes. */

            // Setup.
            var serializer = BuildSerializer(missingOriginDocument: configuredMode);
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
            Assert.Equal(expectedMode, referenceableResult.MissingOriginDocument);
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
        public void SerializeDoesNotLazyLoadASummary()
        {
            /* SCR-279: the extra elements bag is never loaded data, so a summary never counts
             * it among its loaded members. Writing a summary reads the bag twice, clearing it
             * and through the class map extra elements write: neither read may load the
             * origin document, or every reference write would cost one query per summary. */

            // Setup.
            var serializer = BuildSerializer(mm => mm.MapMember(m => m.StringProp));
            dbContextEngineMock.Setup(e => e.ProxyGenerator.IsProxyType(typeof(FakeModelProxy)))
                .Returns(true);
            dbContextEngineMock.Setup(e => e.ProxyGenerator.PurgeProxyType(typeof(FakeModelProxy)))
                .Returns(typeof(FakeModel));

            var sourceDbContextMock = new Mock<IDbContext>();
            sourceDbContextMock.As<IProxyModelsDbContext>();
            var sourceRepositoryMock = new Mock<IRepository>();
            sourceRepositoryMock.Setup(r => r.DbContext)
                .Returns(sourceDbContextMock.Object);
            sourceRepositoryMock.Setup(r => r.TryFindOneAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FakeModel { Id = "idVal", IntegerProp = 42, StringProp = "ok" });

            var model = new FakeModelProxy { Id = "idVal", StringProp = "ok" };
            ((IProxyModel)model).BindProxy(sourceDbContextMock.Object, sourceRepositoryMock.Object);
            ((IReferenceable)model).ClearSettedMembers();
            ((IReferenceable)model).SetAsSummary(["StringProp"], ReactionMode.Warn);

            var serializedDocument = new BsonDocument();
            using var bsonWriter = new BsonDocumentWriter(serializedDocument);

            // Action.
            serializer.Serialize(
                BsonSerializationContext.CreateRoot(bsonWriter),
                new BsonSerializationArgs { NominalType = typeof(FakeModel) },
                model);

            // Assert.
            //the summary wrote its denormalized members without reading its origin document
            sourceRepositoryMock.Verify(r => r.TryFindOneAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.True(((IReferenceable)model).IsSummary);
            var expectedDocument = new BsonDocument
            {
                { "_s", "activeSchemaId" },
                { "_id", "idVal" },
                { "StringProp", "ok" }
            };
            Assert.Equal(0, serializedDocument.CompareTo(expectedDocument));
        }

        [Fact]
        public void SerializeLoadsTheOriginDocumentToCompleteASummary()
        {
            /* SCR-296: a stored reference document can miss a member the active schema declares
             * (an element renamed without bumping the schema id, for instance). Writing that
             * summary reads the member, and the read completes the document being written from
             * the origin document: it runs inside the reference write scope, where the implicit
             * lazy load option never denies it. */

            // Setup.
            var serializer = BuildSerializer(mm => mm.MapMember(m => m.StringProp));
            dbContextEngineMock.Setup(e => e.ProxyGenerator.IsProxyType(typeof(FakeModelProxy)))
                .Returns(true);
            dbContextEngineMock.Setup(e => e.ProxyGenerator.PurgeProxyType(typeof(FakeModelProxy)))
                .Returns(typeof(FakeModel));

            var sourceDbContextMock = new Mock<IDbContext>();
            var proxyModelsDbContextMock = sourceDbContextMock.As<IProxyModelsDbContext>();
            proxyModelsDbContextMock.Setup(c => c.SuppressChangeTracking())
                .Returns(Mock.Of<IDisposable>());
            //the db context decides on the load: capture the scope it sees when the summary asks for it
            var writingReferenceOnLoad = false;
            proxyModelsDbContextMock.Setup(c => c.OnImplicitLazyLoad(It.IsAny<Type>(), It.IsAny<string?>()))
                .Callback(() => writingReferenceOnLoad =
                    ReferenceWriteHandler.IsWritingReference(AsyncLocalContext.Instance));

            var sourceRepositoryMock = new Mock<IRepository>();
            sourceRepositoryMock.Setup(r => r.DbContext)
                .Returns(sourceDbContextMock.Object);
            sourceRepositoryMock.Setup(r => r.TryFindOneAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FakeModel { Id = "idVal", StringProp = "ok" });

            //the summary carries no StringProp: the reference document didn't have its element
            var model = new FakeModelProxy { Id = "idVal" };
            ((IProxyModel)model).BindProxy(sourceDbContextMock.Object, sourceRepositoryMock.Object);
            ((IReferenceable)model).ClearSettedMembers();
            ((IReferenceable)model).SetAsSummary([], ReactionMode.Warn);

            var serializedDocument = new BsonDocument();
            using var bsonWriter = new BsonDocumentWriter(serializedDocument);
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            // Action.
            serializer.Serialize(
                BsonSerializationContext.CreateRoot(bsonWriter),
                new BsonSerializationArgs { NominalType = typeof(FakeModel) },
                model);

            // Assert.
            //the write loaded the origin document, reporting the load as a reference write one
            sourceRepositoryMock.Verify(
                r => r.TryFindOneAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()),
                Times.Once);
            Assert.True(writingReferenceOnLoad);
            //and wrote the member it completed, instead of its default value
            var expectedDocument = new BsonDocument
            {
                { "_s", "activeSchemaId" },
                { "_id", "idVal" },
                { "StringProp", "ok" }
            };
            Assert.Equal(0, serializedDocument.CompareTo(expectedDocument));
        }

        [Fact]
        public void SerializeThrowsOnReferredModelWithoutId()
        {
            /* SCR-164: a reference document without id deserializes to null, so writing it
             * would silently lose the link. Outside of a new referred models discovery pass
             * (where the model is collected for auto creation instead), serializing a
             * reference to a model without id fails loudly. */

            // Setup.
            var serializer = BuildSerializer();
            var model = new FakeModel { StringProp = "ok" }; //no id assigned

            var serializedDocument = new BsonDocument();
            using var bsonWriter = new BsonDocumentWriter(serializedDocument);

            // Action.
            var exception = Assert.Throws<InvalidOperationException>(() =>
                serializer.Serialize(
                    BsonSerializationContext.CreateRoot(bsonWriter),
                    new BsonSerializationArgs { NominalType = typeof(FakeModel) },
                    model));

            // Assert.
            Assert.Contains("without id", exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(FakeModel), exception.Message, StringComparison.Ordinal);
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
        private ReferenceSerializer<FakeModel, string> BuildSerializer(
            Action<BsonClassMap<FakeModel>>? fakeModelInitializer = null,
            ReactionMode? missingOriginDocument = null) =>
            new(dbContextEngineMock.Object, config =>
            {
                if (missingOriginDocument.HasValue)
                    config.MissingOriginDocument = missingOriginDocument.Value;

                //the auto mapped ModelBase map carries the extra elements member, as in real configurations
                config.AddModelMap<ModelBase>("modelBaseSchemaId");
                config.AddModelMap<FakeEntityModelBase<string>>("baseSchemaId", mm => mm.MapIdMember(m => m.Id));
                config.AddModelMap<FakeModel>("activeSchemaId", fakeModelInitializer ?? (mm => mm.AutoMap()));
            });
    }
}
