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
using Etherna.MongODM.Core.Extensions;
using Etherna.MongODM.Core.Models;
using Etherna.MongODM.Core.Options;
using Etherna.MongODM.Core.Serialization.Mapping;
using Etherna.MongODM.Core.Serialization.Serializers;
using Moq;
using System;
using System.Reflection;
using Xunit;

namespace Etherna.MongODM.Core
{
    public class ReferenceSerializerConfigurationTest
    {
        // Fields.
        private readonly Mock<IDbContextEngine> dbContextEngineMock = new();

        // Constructor.
        public ReferenceSerializerConfigurationTest()
        {
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
    }
}
