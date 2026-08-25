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
using Etherna.Scrinium.Core.Extensions;
using Etherna.Scrinium.Core.Models;
using Etherna.Scrinium.Core.Options;
using Etherna.Scrinium.Core.Serialization.Mapping;
using Etherna.Scrinium.Core.Serialization.Serializers;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Etherna.Scrinium.Core
{
    public class ReferenceSerializerConfigurationTest
    {
        // Fields.
        private readonly Mock<IDbContextEngine> dbContextEngineMock = new();
        private readonly Mock<ILogger> loggerMock = new();

        // Constructor.
        public ReferenceSerializerConfigurationTest()
        {
            loggerMock.Setup(l => l.IsEnabled(It.IsAny<LogLevel>()))
                .Returns(true);

            dbContextEngineMock.Setup(e => e.Logger)
                .Returns(() => loggerMock.Object);
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
        }

        // Tests.
        [Fact]
        public void BuilderChainsSchemaConfigurationMethods()
        {
            // Setup.
            var configuration = BuildConfiguration(config =>
            {
                config.AddModelMap<FakeEntityModelBase<string>>("baseSchemaId", mm => mm.MapIdMember(m => m.Id));
                config.AddModelMap<FakeModel>("activeSchemaId", mm => mm.MapMember(m => m.StringProp))
                    .AddSecondarySchema("secondarySchemaId", mm => mm.MapMember(m => m.IntegerProp))
                    .AddFallbackSchema(mm => mm.MapMember(m => m.StringProp));
            });

            // Action.
            var secondarySerializer = configuration.GetSerializer(typeof(FakeModel), "secondarySchemaId");
            var fallbackSerializer = configuration.GetSerializer(typeof(FakeModel), "unknownSchemaId");

            // Assert.
            //each chained call configures the same model map
            var secondaryModel = DeserializeFakeModel(secondarySerializer, new BsonDocument
            {
                { "_id", "idVal" },
                { "IntegerProp", 42 }
            });
            Assert.Equal("idVal", secondaryModel.Id);
            Assert.Equal(42, secondaryModel.IntegerProp);

            var fallbackModel = DeserializeFakeModel(fallbackSerializer, new BsonDocument
            {
                { "_id", "idVal" },
                { "StringProp", "ok" }
            });
            Assert.Equal("idVal", fallbackModel.Id);
            Assert.Equal("ok", fallbackModel.StringProp);
        }

        [Fact]
        public void GetSerializerBoundsReportedUnrecognizedSchemaIds()
        {
            /* MODM-237: schema ids come from documents, so remembering every reported one would
             * let whoever writes the reference documents grow the reported ids set without any
             * bound: the reports stop at the bound. */

            // Setup.
            var configuration = BuildConfiguration(config =>
            {
                config.AddModelMap<FakeEntityModelBase<string>>("baseSchemaId", mm => mm.MapIdMember(m => m.Id));
                config.AddModelMap<FakeModel>("activeSchemaId", mm => mm.MapMember(m => m.StringProp));
            });

            // Action.
            //resolve the serializer of documents carrying more distinct unrecognized ids than the bound
            for (var i = 0; i < ReferenceSerializerConfiguration.MaxWarnedUnrecognizedSchemaIds * 2; i++)
                configuration.GetSerializer(typeof(FakeModel), "unknownSchemaId" + i.ToString(CultureInfo.InvariantCulture));

            // Assert.
            VerifyUnrecognizedSchemaIdWarnings(
                Times.Exactly(ReferenceSerializerConfiguration.MaxWarnedUnrecognizedSchemaIds));
        }

        [Fact]
        public void GetSerializerDeserializesEmptyModelWithUnknownSchemaIdAndMissingIdElement()
        {
            // Setup.
            var configuration = BuildConfiguration(config =>
                config.AddModelMap<FakeModel>("activeSchemaId", mm => mm.MapMember(m => m.StringProp)));

            // Action.
            var serializer = configuration.GetSerializer(typeof(FakeModel), "unknownSchemaId");

            // Assert.
            //without a mapped id and no conventional "_id" element, nothing is read:
            //a reference missing its id can't be resolved anyway
            var model = DeserializeFakeModel(serializer, new BsonDocument
            {
                { "StringProp", "ok" }
            });

            Assert.Null(model.Id);
            Assert.Null(model.StringProp);
        }

        [Fact]
        public void GetSerializerHonorsFallbackSchemaWithUnknownSchemaId()
        {
            // Setup.
            var configuration = BuildConfiguration(config =>
            {
                config.AddModelMap<FakeEntityModelBase<string>>("baseSchemaId", mm => mm.MapIdMember(m => m.Id));
                config.AddModelMap<FakeModel>("activeSchemaId", mm => mm.MapMember(m => m.IntegerProp))
                    .AddFallbackSchema(mm => mm.MapMember(m => m.StringProp));
            });

            // Action.
            var serializer = configuration.GetSerializer(typeof(FakeModel), "unknownSchemaId");

            // Assert.
            //the fallback schema deserializes its mapped members, not only the id
            var model = DeserializeFakeModel(serializer, new BsonDocument
            {
                { "_id", "idVal" },
                { "StringProp", "ok" }
            });

            Assert.Equal("idVal", model.Id);
            Assert.Equal("ok", model.StringProp);
            //a configured fallback is the declared handling of unrecognized ids: nothing to report
            VerifyUnrecognizedSchemaIdWarnings(Times.Never());
        }

        [Fact]
        public void GetSerializerHonorsFallbackSerializerWithUnknownSchemaId()
        {
            // Setup.
            var classMap = new BsonClassMap<FakeModel>(cm => cm.AutoMap());
            classMap.Freeze();
            var fallbackSerializer = (IBsonSerializer<FakeModel>)classMap.ToSerializer();

            var configuration = BuildConfiguration(config =>
            {
                config.AddModelMap<FakeEntityModelBase<string>>("baseSchemaId", mm => mm.MapIdMember(m => m.Id));
                config.AddModelMap<FakeModel>("activeSchemaId", mm => mm.MapMember(m => m.IntegerProp))
                    .AddFallbackCustomSerializer(fallbackSerializer);
            });

            // Action.
            var serializer = configuration.GetSerializer(typeof(FakeModel), "unknownSchemaId");

            // Assert.
            Assert.Same(fallbackSerializer, serializer);
            //a configured fallback is the declared handling of unrecognized ids: nothing to report
            VerifyUnrecognizedSchemaIdWarnings(Times.Never());
        }

        [Fact]
        public void GetSerializerSelectsSchemaSerializerByKnownSchemaId()
        {
            // Setup.
            var configuration = BuildConfiguration(config =>
            {
                config.AddModelMap<FakeEntityModelBase<string>>("baseSchemaId", mm => mm.MapIdMember(m => m.Id));
                config.AddModelMap<FakeModel>("activeSchemaId", mm => mm.MapMember(m => m.StringProp))
                    .AddSecondarySchema("secondarySchemaId", mm => mm.MapMember(m => m.IntegerProp));
            });

            // Action.
            var serializer = configuration.GetSerializer(typeof(FakeModel), "secondarySchemaId");

            // Assert.
            var model = DeserializeFakeModel(serializer, new BsonDocument
            {
                { "_id", "idVal" },
                { "IntegerProp", 42 }
            });

            Assert.Equal("idVal", model.Id);
            Assert.Equal(42, model.IntegerProp);
            //a document selecting a registered schema is the versioned schemas behavior: nothing to report
            VerifyUnrecognizedSchemaIdWarnings(Times.Never());
        }

        [Fact]
        public void GetSerializerTriesConventionalIdElementWithUnknownSchemaIdAndNoMappedId()
        {
            // Setup.
            var configuration = BuildConfiguration(config =>
            {
                config.AddModelMap<FakeEntityModelBase<string>>("baseSchemaId", mm => mm.MapMember(m => m.Id).SetElementName("_id"));
                config.AddModelMap<FakeModel>("activeSchemaId", mm => mm.MapMember(m => m.StringProp));
            });

            // Action.
            var serializer = configuration.GetSerializer(typeof(FakeModel), "unknownSchemaId");

            // Assert.
            //the conventional "_id" element is read also without a mapped id member
            var model = DeserializeFakeModel(serializer, new BsonDocument
            {
                { "_id", "idVal" },
                { "StringProp", "ok" }
            });

            Assert.Equal("idVal", model.Id);
            Assert.Null(model.StringProp);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("unknownSchemaId")]
        public void GetSerializerUsesIdOnlyFallbackWithUnrecognizedSchemaId(string? modelMapSchemaId)
        {
            // Setup.
            var configuration = BuildConfiguration(config =>
            {
                config.AddModelMap<FakeEntityModelBase<string>>("baseSchemaId", mm => mm.MapIdMember(m => m.Id));
                config.AddModelMap<FakeModel>("activeSchemaId", mm => mm.MapMember(m => m.StringProp));
            });

            // Action.
            var serializer = configuration.GetSerializer(typeof(FakeModel), modelMapSchemaId);

            // Assert.
            //only the id is read, ignoring any other element, also with incompatible values
            var model = DeserializeFakeModel(serializer, new BsonDocument
            {
                { "_id", "idVal" },
                { "StringProp", 42 },
                { "UnknownElement", "ignored" }
            });

            Assert.Equal("idVal", model.Id);
            Assert.Null(model.StringProp);
            //the id-only degradation of an unrecognized id is reported, also when the id is missing
            VerifyUnrecognizedSchemaIdWarnings(Times.Once());
        }

        [Fact]
        public void GetSerializerWarnsOnceOnUnrecognizedSchemaId()
        {
            /* MODM-237: the schema id is document content. An id matching no registered schema,
             * with no fallback configured, degrades the read to the reference id alone, and every
             * member access of the resulting summary lazy loads the whole origin document: report
             * it once per model type and id, instead of amplifying reads silently. */

            // Setup.
            var configuration = BuildConfiguration(config =>
            {
                config.AddModelMap<FakeEntityModelBase<string>>("baseSchemaId", mm => mm.MapIdMember(m => m.Id));
                config.AddModelMap<FakeModel>("activeSchemaId", mm => mm.MapMember(m => m.StringProp));
            });

            // Action.
            //resolve the serializer of documents carrying the same unrecognized id twice
            for (var i = 0; i < 2; i++)
                configuration.GetSerializer(typeof(FakeModel), "unknownSchemaId");

            // Assert.
            //the warning names the model type and the unrecognized id, and reports only once
            VerifyUnrecognizedSchemaIdWarnings(Times.Once(), nameof(FakeModel), "unknownSchemaId");
        }

        [Fact]
        public void TryGetSummaryLoadedMemberNamesMapsDocumentElementsWithKnownSchemaId()
        {
            // Setup.
            var configuration = BuildConfiguration(config =>
            {
                config.AddModelMap<FakeEntityModelBase<string>>("baseSchemaId", mm => mm.MapIdMember(m => m.Id));
                config.AddModelMap<FakeModel>("activeSchemaId", mm =>
                {
                    mm.MapMember(m => m.IntegerProp);
                    mm.MapMember(m => m.StringProp);
                });
            });
            var document = new BsonDocument
            {
                { "_s", "activeSchemaId" },
                { "_id", "idVal" },
                { "StringProp", "ok" },
                { "removedProp", "extraVal" }
            };

            // Action.
            var memberNames = configuration.TryGetSummaryLoadedMemberNames(typeof(FakeModel), "activeSchemaId", document);

            // Assert.
            Assert.NotNull(memberNames);
            Assert.Contains("StringProp", memberNames);
            //the id never joins the summary member names
            Assert.DoesNotContain("Id", memberNames);
            //a mapped member absent from the document is not loaded
            Assert.DoesNotContain("IntegerProp", memberNames);
            //an unmapped element maps to no member
            Assert.DoesNotContain("removedProp", memberNames);
        }

        [Fact]
        public void TryGetSummaryLoadedMemberNamesMapsWithFallbackSchemaWithUnknownSchemaId()
        {
            // Setup.
            var configuration = BuildConfiguration(config =>
            {
                config.AddModelMap<FakeEntityModelBase<string>>("baseSchemaId", mm => mm.MapIdMember(m => m.Id));
                config.AddModelMap<FakeModel>("activeSchemaId", mm => mm.MapMember(m => m.IntegerProp))
                    .AddFallbackSchema(mm => mm.MapMember(m => m.StringProp));
            });
            var document = new BsonDocument
            {
                { "_id", "idVal" },
                { "IntegerProp", 42 },
                { "StringProp", "ok" }
            };

            // Action.
            var memberNames = configuration.TryGetSummaryLoadedMemberNames(typeof(FakeModel), "unknownSchemaId", document);

            // Assert.
            //only the members mapped by the fallback schema join, not the active schema ones
            Assert.NotNull(memberNames);
            Assert.Contains("StringProp", memberNames);
            Assert.DoesNotContain("Id", memberNames);
            Assert.DoesNotContain("IntegerProp", memberNames);
        }

        [Fact]
        public void TryGetSummaryLoadedMemberNamesReturnsEmptyWithIdOnlyFallback()
        {
            // Setup.
            var configuration = BuildConfiguration(config =>
            {
                config.AddModelMap<FakeEntityModelBase<string>>("baseSchemaId", mm => mm.MapIdMember(m => m.Id));
                config.AddModelMap<FakeModel>("activeSchemaId", mm => mm.MapMember(m => m.StringProp));
            });
            var document = new BsonDocument
            {
                { "_id", "idVal" },
                { "StringProp", "ok" }
            };

            // Action.
            var memberNames = configuration.TryGetSummaryLoadedMemberNames(typeof(FakeModel), "unknownSchemaId", document);

            // Assert.
            //the default fallback reads only the reference id, never joining the summary member names
            Assert.NotNull(memberNames);
            Assert.Empty(memberNames);
        }

        [Fact]
        public void TryGetSummaryLoadedMemberNamesReturnsNullWithFallbackSerializer()
        {
            // Setup.
            var classMap = new BsonClassMap<FakeModel>(cm => cm.AutoMap());
            classMap.Freeze();

            var configuration = BuildConfiguration(config =>
            {
                config.AddModelMap<FakeEntityModelBase<string>>("baseSchemaId", mm => mm.MapIdMember(m => m.Id));
                config.AddModelMap<FakeModel>("activeSchemaId", mm => mm.MapMember(m => m.IntegerProp))
                    .AddFallbackCustomSerializer((IBsonSerializer<FakeModel>)classMap.ToSerializer());
            });
            var document = new BsonDocument
            {
                { "_id", "idVal" },
                { "StringProp", "ok" }
            };

            // Action.
            var memberNames = configuration.TryGetSummaryLoadedMemberNames(typeof(FakeModel), "unknownSchemaId", document);

            // Assert.
            //a custom fallback serializer has no schema mapping elements to members
            Assert.Null(memberNames);
        }

        // Helpers.
        private ReferenceSerializerConfiguration BuildConfiguration(Action<ReferenceSerializerConfiguration> configure) =>
            new ReferenceSerializer<FakeModel, string>(dbContextEngineMock.Object, configure).Configuration;

        private static FakeModel DeserializeFakeModel(IBsonSerializer serializer, BsonDocument document)
        {
            var bsonReader = new BsonDocumentReader(document);
            return (FakeModel)serializer.Deserialize(
                BsonDeserializationContext.CreateRoot(bsonReader),
                new BsonDeserializationArgs { NominalType = typeof(FakeModel) });
        }

        private void VerifyUnrecognizedSchemaIdWarnings(Times times, params string[] expectedMessageContents) =>
            loggerMock.Verify(l => l.Log(
                    LogLevel.Warning,
                    It.Is<EventId>(id => id.Name == nameof(Extensions.LoggerExtensions.ReferenceSerializerUnrecognizedSchemaId)),
                    It.Is<It.IsAnyType>((state, _) => expectedMessageContents.All(
                        content => state.ToString()!.Contains(content, StringComparison.Ordinal))),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                times);
    }
}
