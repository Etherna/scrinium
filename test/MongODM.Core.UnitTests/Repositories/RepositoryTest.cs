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
using Etherna.MongoDB.Driver;
using Etherna.MongODM.Core.ExecContext;
using Etherna.MongODM.Core.Models;
using Etherna.MongODM.Core.Options;
using Etherna.MongODM.Core.Serialization.Mapping;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.MongODM.Core.Repositories
{
    public class RepositoryTest
    {
        // Fields.
        private readonly Mock<IMongoCollection<FakeModel>> collectionMock = new();
        private readonly Mock<IDbContext> dbContextMock = new();
        private readonly Mock<IDbContextEngine> engineMock = new();
        private readonly Mock<IExecutionContext> executionContextMock = new();
        private readonly Mock<IMapRegistry> mapRegistryMock = new();
        private readonly Mock<IModelMap> modelMapMock = new();
        private readonly Mock<IDbContextOptions> optionsMock = new();

        // Constructor.
        public RepositoryTest()
        {
            /* Model map exposing the id member maps of two referenced documents: a single
             * reference member, and an enumerable of references. */
            var classMap = new BsonClassMap<FakeModel>(cm =>
            {
                cm.MapMember(m => m.EnumerableProp);
                cm.MapMember(m => m.ObjectProp);
            });
            classMap.Freeze();
            //the id is mapped on the level declaring it
            var idClassMap = new BsonClassMap<FakeEntityModelBase<string>>(cm => cm.MapIdMember(m => m.Id));
            idClassMap.Freeze();
            modelMapMock.Setup(m => m.AllDescendingMemberMaps)
                .Returns([
                    ReferenceIdMemberMap(classMap.GetMemberMap(m => m.ObjectProp), idClassMap.IdMemberMap!),
                    ReferenceIdMemberMap(classMap.GetMemberMap(m => m.EnumerableProp), idClassMap.IdMemberMap!)
                ]);
            var modelMap = modelMapMock.Object;
            mapRegistryMock.Setup(r => r.TryGetModelMap(It.IsAny<Type>(), out modelMap))
                .Returns(true);

            /* Index keys render against the collection serializers: a document serializer
             * that can't resolve the members renders the field paths verbatim. */
            collectionMock.Setup(c => c.DocumentSerializer)
                .Returns(new Mock<IBsonSerializer<FakeModel>>().Object);
            collectionMock.Setup(c => c.Settings)
                .Returns(new MongoCollectionSettings { SerializerRegistry = new BsonSerializerRegistry() });

            executionContextMock.Setup(c => c.Items).Returns(new Dictionary<object, object?>());
            optionsMock.Setup(o => o.DbName).Returns("test-db");
            engineMock.Setup(e => e.ExecutionContext).Returns(executionContextMock.Object);
            engineMock.Setup(e => e.MapRegistry).Returns(mapRegistryMock.Object);
            engineMock.Setup(e => e.Options).Returns(optionsMock.Object);
            engineMock.Setup(e => e.GetMongoCollection<FakeModel>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>(), It.IsAny<bool>()))
                .Returns(collectionMock.Object);
            dbContextMock.Setup(c => c.Engine).Returns(engineMock.Object);
        }

        // Tests.
        [Fact]
        public async Task DefinedIndexesBuildAnAutomaticIndexForEachReferenceIdPath()
        {
            // Setup.
            var repository = BuildRepository();

            // Action.
            var indexes = await repository.GetDefinedIndexModelsAsync();

            // Assert.
            Assert.Equal(
                ["ref_ObjectProp._id", "ref_EnumerableProp._id"],
                indexes.Select(i => i.Options.Name));
        }

        [Fact]
        public async Task DefinedIndexesKeepTheAutomaticIndexOnAReferenceIdPathIndexedWithoutASortOrder()
        {
            /* Only an ascending or descending key serves every query on its field: a hashed
             * or text key on the reference id path doesn't replace the automatic index. */

            // Setup.
            var repository = BuildRepository(
                (Builders<FakeModel>.IndexKeys.Hashed("ObjectProp._id"),
                    new CreateIndexOptions<FakeModel> { Name = "custom" }));

            // Action.
            var indexes = await repository.GetDefinedIndexModelsAsync();

            // Assert.
            Assert.Equal(
                ["custom", "ref_ObjectProp._id", "ref_EnumerableProp._id"],
                indexes.Select(i => i.Options.Name));
        }

        [Fact]
        public async Task DefinedIndexesKeepTheAutomaticIndexOnAReferenceIdPathNotOpeningACustomIndex()
        {
            /* A compound index doesn't serve the queries on a field following its first key:
             * the automatic index on the reference id path is not a duplicate of it. */

            // Setup.
            var repository = BuildRepository(
                (Builders<FakeModel>.IndexKeys.Ascending("StringProp").Ascending("ObjectProp._id"),
                    new CreateIndexOptions<FakeModel> { Name = "custom" }));

            // Action.
            var indexes = await repository.GetDefinedIndexModelsAsync();

            // Assert.
            Assert.Equal(
                ["custom", "ref_ObjectProp._id", "ref_EnumerableProp._id"],
                indexes.Select(i => i.Options.Name));
        }

        [Fact]
        public async Task DefinedIndexesSkipTheAutomaticIndexOnAReferenceIdPathIndexedByACustomIndex()
        {
            // Setup.
            var repository = BuildRepository(
                (Builders<FakeModel>.IndexKeys.Ascending("ObjectProp._id"),
                    new CreateIndexOptions<FakeModel> { Unique = true }));

            // Action.
            var indexes = await repository.GetDefinedIndexModelsAsync();

            // Assert.
            Assert.Equal(
                ["doc_ObjectProp._id", "ref_EnumerableProp._id"],
                indexes.Select(i => i.Options.Name));
        }

        [Fact]
        public async Task DefinedIndexesSkipTheAutomaticIndexOnAReferenceIdPathOpeningACompoundCustomIndex()
        {
            // Setup.
            var repository = BuildRepository(
                (Builders<FakeModel>.IndexKeys.Descending("ObjectProp._id").Ascending("StringProp"),
                    new CreateIndexOptions<FakeModel> { Name = "custom" }));

            // Action.
            var indexes = await repository.GetDefinedIndexModelsAsync();

            // Assert.
            Assert.Equal(
                ["custom", "ref_EnumerableProp._id"],
                indexes.Select(i => i.Options.Name));
        }

        // Helpers.
        private Repository<FakeModel, string> BuildRepository(
            params (IndexKeysDefinition<FakeModel> keys, CreateIndexOptions<FakeModel> options)[] indexBuilders)
        {
            var repository = new Repository<FakeModel, string>(
                new RepositoryOptions<FakeModel>("fakeModels") { IndexBuilders = indexBuilders });
            repository.Initialize(dbContextMock.Object, new Mock<ILogger>().Object);
            return repository;
        }

        private static IMemberMap ReferenceIdMemberMap(params BsonMemberMap[] elementPath)
        {
            var memberMapPath = elementPath.Select(bsonMemberMap =>
            {
                var pathMemberMapMock = new Mock<IMemberMap>();
                pathMemberMapMock.Setup(mm => mm.BsonMemberMap).Returns(bsonMemberMap);
                return pathMemberMapMock.Object;
            }).ToArray();

            var memberMapMock = new Mock<IMemberMap>();
            memberMapMock.Setup(mm => mm.IsEntityReferenceMember).Returns(true);
            memberMapMock.Setup(mm => mm.IsIdMember).Returns(true);
            memberMapMock.Setup(mm => mm.MemberMapPath).Returns(memberMapPath);
            return memberMapMock.Object;
        }
    }
}
