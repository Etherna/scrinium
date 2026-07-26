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
using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.Exceptions;
using Etherna.MongODM.Core.Models;
using Etherna.MongODM.Core.Options;
using Etherna.MongODM.Core.Serialization.Mapping;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Linq;
using Xunit;

namespace Etherna.MongODM.Core
{
    public class MapRegistryTest
    {
        // Internal classes.
        public class FirstModel
        {
            public string? Name { get; set; }
        }
        public class SecondModel
        {
            public string? Name { get; set; }
        }
        public class WrongIdModel : FakeEntityModelBase<string>
        {
            public virtual string? Code { get; set; }
        }

        // Fields.
        private readonly Mock<IDbContextEngine> dbContextEngineMock = new();
        private readonly MapRegistry mapRegistry = new();

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
                .Returns(new BsonSerializerRegistry());

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

            var entityModelMap = (IModelMap)mapRegistry.AddModelMap<FakeModel>("fakeSchemaId");
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
        public void FreezeDoesntCreateProxiesNorProxyMaps()
        {
            /* MODM-189: model maps register only model types: the schema discovery doesn't
             * create proxy instances, and proxy types have no maps of their own. */

            // Setup.
            mapRegistry.AddModelMap<FakeModel>("fakeSchemaId");

            // Action.
            mapRegistry.Freeze();

            // Assert.
            dbContextEngineMock.Verify(
                e => e.ProxyGenerator.CreateInstance(It.IsAny<Type>(), It.IsAny<object[]>()),
                Times.Never());
            Assert.Equal(
                new[] { typeof(FakeModel), typeof(FakeEntityModelBase<string>), typeof(ModelBase), typeof(object) }.OrderBy(t => t.FullName),
                mapRegistry.MapsByModelType.Keys.OrderBy(t => t.FullName));
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

        // Helpers.
        private static TModel DeserializeModel<TModel>(IBsonSerializer serializer, BsonDocument document)
        {
            var bsonReader = new BsonDocumentReader(document);
            return (TModel)serializer.Deserialize(
                BsonDeserializationContext.CreateRoot(bsonReader),
                new BsonDeserializationArgs { NominalType = typeof(TModel) });
        }
    }
}
