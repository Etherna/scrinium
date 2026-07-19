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
            dbContextEngineMock.Setup(e => e.Options.ModelMapVersion)
                .Returns(new ModelMapVersionOptions());
            dbContextEngineMock.Setup(e => e.ProxyGenerator.IsProxyType(It.IsAny<Type>()))
                .Returns(true);
            dbContextEngineMock.Setup(e => e.ProxyGenerator.PurgeProxyType(It.IsAny<Type>()))
                .Returns<Type>(t => t);
        }

        // Tests.
        [Fact]
        public void GetSerializerDeserializesEmptyModelWithUnknownSchemaIdAndMissingIdElement()
        {
            // Setup.
            var configuration = BuildConfiguration(config =>
                config.AddModelMap<FakeModel>("activeMapId", mm => mm.MapMember(m => m.StringProp)));

            // Action.
            var serializer = configuration.GetSerializer(typeof(FakeModel), "unknownMapId");

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
                config.AddModelMap<FakeEntityModelBase<string>>("baseMapId", mm => mm.MapIdMember(m => m.Id));
                config.AddModelMap<FakeModel>("activeMapId", mm => mm.MapMember(m => m.IntegerProp))
                    .AddFallbackSchema(mm => mm.MapMember(m => m.StringProp));
            });

            // Action.
            var serializer = configuration.GetSerializer(typeof(FakeModel), "unknownMapId");

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
                config.AddModelMap<FakeEntityModelBase<string>>("baseMapId", mm => mm.MapIdMember(m => m.Id));
                config.AddModelMap<FakeModel>("activeMapId", mm => mm.MapMember(m => m.IntegerProp))
                    .AddFallbackCustomSerializer(fallbackSerializer);
            });

            // Action.
            var serializer = configuration.GetSerializer(typeof(FakeModel), "unknownMapId");

            // Assert.
            Assert.Same(fallbackSerializer, serializer);
        }

        [Fact]
        public void GetSerializerSelectsSchemaSerializerByKnownSchemaId()
        {
            // Setup.
            var configuration = BuildConfiguration(config =>
            {
                config.AddModelMap<FakeEntityModelBase<string>>("baseMapId", mm => mm.MapIdMember(m => m.Id));
                config.AddModelMap<FakeModel>("activeMapId", mm => mm.MapMember(m => m.StringProp))
                    .AddSecondarySchema("secondaryMapId", mm => mm.MapMember(m => m.IntegerProp));
            });

            // Action.
            var serializer = configuration.GetSerializer(typeof(FakeModel), "secondaryMapId");

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
                config.AddModelMap<FakeEntityModelBase<string>>("baseMapId", mm => mm.MapMember(m => m.Id).SetElementName("_id"));
                config.AddModelMap<FakeModel>("activeMapId", mm => mm.MapMember(m => m.StringProp));
            });

            // Action.
            var serializer = configuration.GetSerializer(typeof(FakeModel), "unknownMapId");

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
        [InlineData("unknownMapId")]
        public void GetSerializerUsesIdOnlyFallbackWithUnrecognizedSchemaId(string? modelMapSchemaId)
        {
            // Setup.
            var configuration = BuildConfiguration(config =>
            {
                config.AddModelMap<FakeEntityModelBase<string>>("baseMapId", mm => mm.MapIdMember(m => m.Id));
                config.AddModelMap<FakeModel>("activeMapId", mm => mm.MapMember(m => m.StringProp));
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
