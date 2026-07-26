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
using Etherna.MongoDB.Driver;
using Etherna.MongoDB.Driver.Search;
using Etherna.MongODM.Core.ExecContext.AsyncLocal;
using Etherna.MongODM.Core.Models;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.MongODM.Core.Utility
{
    public class LimitedAccessMongoCollectionTest
    {
        // Fields.
        private readonly Mock<IMongoCollection<FakeModel>> collectionMock = new();
        private readonly Mock<IDbContextEngine> engineMock = new();
        private readonly Mock<IMongoIndexManager<FakeModel>> indexManagerMock = new();
        private readonly Mock<IMongoSearchIndexManager> searchIndexManagerMock = new();

        // Constructor.
        public LimitedAccessMongoCollectionTest()
        {
            collectionMock.Setup(c => c.Indexes)
                .Returns(indexManagerMock.Object);
            collectionMock.Setup(c => c.SearchIndexes)
                .Returns(searchIndexManagerMock.Object);
            engineMock.Setup(e => e.ExecutionContext)
                .Returns(AsyncLocalContext.Instance);
        }

        // Tests.
        [Fact]
        public async Task ForeignExclusiveAccessDeniesIndexWrites()
        {
            // Setup.
            /* An exclusive write lock is active on the engine, and the current flow holds
             * no exclusive access allowance. */
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            engineMock.Setup(e => e.IsExclusiveWriteEnabled)
                .Returns(true);
            var collection = new LimitedAccessMongoCollection<FakeModel>(engineMock.Object, collectionMock.Object, false);

            // Action and assert.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => collection.Indexes.CreateOneAsync(
                new CreateIndexModel<FakeModel>(Builders<FakeModel>.IndexKeys.Ascending(m => m.IntegerProp))));
            indexManagerMock.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ReadOnlyCollectionAllowsIndexListing()
        {
            // Setup.
            var collection = new LimitedAccessMongoCollection<FakeModel>(engineMock.Object, collectionMock.Object, true);
            var indexCursor = new Mock<IAsyncCursor<BsonDocument>>().Object;
            var searchIndexCursor = new Mock<IAsyncCursor<BsonDocument>>().Object;
            indexManagerMock.Setup(m => m.ListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(indexCursor);
            searchIndexManagerMock.Setup(m => m.List(It.IsAny<string?>(), It.IsAny<AggregateOptions?>(), It.IsAny<CancellationToken>()))
                .Returns(searchIndexCursor);

            // Action.
            var listedIndexes = await collection.Indexes.ListAsync();
            var listedSearchIndexes = collection.SearchIndexes.List();

            // Assert.
            Assert.Same(indexCursor, listedIndexes);
            Assert.Same(searchIndexCursor, listedSearchIndexes);
        }

        [Fact]
        public async Task ReadOnlyCollectionAllowsReads()
        {
            // Setup.
            var collection = new LimitedAccessMongoCollection<FakeModel>(engineMock.Object, collectionMock.Object, true);
            var cursor = new Mock<IAsyncCursor<FakeModel>>().Object;
            collectionMock.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<FakeModel>>(), It.IsAny<FindOptions<FakeModel, FakeModel>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursor);
            collectionMock.Setup(c => c.CountDocumentsAsync(It.IsAny<FilterDefinition<FakeModel>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(42);

            // Action.
            var foundCursor = await collection.FindAsync(Builders<FakeModel>.Filter.Empty);
            var documentsCount = await collection.CountDocumentsAsync(Builders<FakeModel>.Filter.Empty);

            // Assert.
            Assert.Same(cursor, foundCursor);
            Assert.Equal(42, documentsCount);
        }

        [Fact]
        public async Task ReadOnlyCollectionDeniesIndexManagement()
        {
            // Setup.
            var collection = new LimitedAccessMongoCollection<FakeModel>(engineMock.Object, collectionMock.Object, true);
            var indexModel = new CreateIndexModel<FakeModel>(Builders<FakeModel>.IndexKeys.Ascending(m => m.IntegerProp));

            // Action and assert.
            var indexes = collection.Indexes;
            Assert.Throws<UnauthorizedAccessException>(() => indexes.CreateMany([indexModel]));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => indexes.CreateManyAsync([indexModel]));
            Assert.Throws<UnauthorizedAccessException>(() => indexes.CreateOne(indexModel));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => indexes.CreateOneAsync(indexModel));
            Assert.Throws<UnauthorizedAccessException>(() => indexes.DropAll());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => indexes.DropAllAsync());
            Assert.Throws<UnauthorizedAccessException>(() => indexes.DropOne("indexName"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => indexes.DropOneAsync("indexName"));
            indexManagerMock.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ReadOnlyCollectionDeniesSearchIndexManagement()
        {
            // Setup.
            var collection = new LimitedAccessMongoCollection<FakeModel>(engineMock.Object, collectionMock.Object, true);

            // Action and assert.
            var searchIndexes = collection.SearchIndexes;
            Assert.Throws<UnauthorizedAccessException>(() => searchIndexes.CreateOne(new BsonDocument()));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => searchIndexes.CreateOneAsync(new BsonDocument()));
            Assert.Throws<UnauthorizedAccessException>(() => searchIndexes.DropOne("indexName"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => searchIndexes.DropOneAsync("indexName"));
            Assert.Throws<UnauthorizedAccessException>(() => searchIndexes.Update("indexName", new BsonDocument()));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => searchIndexes.UpdateAsync("indexName", new BsonDocument()));
            searchIndexManagerMock.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ReadOnlyCollectionDeniesWrites()
        {
            // Setup.
            var collection = new LimitedAccessMongoCollection<FakeModel>(engineMock.Object, collectionMock.Object, true);
            var model = new FakeModel();

            // Action and assert.
            Assert.Throws<UnauthorizedAccessException>(() => collection.BulkWrite([new InsertOneModel<FakeModel>(model)]));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => collection.DeleteManyAsync(Builders<FakeModel>.Filter.Empty));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => collection.DeleteOneAsync(Builders<FakeModel>.Filter.Empty));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => collection.FindOneAndDeleteAsync(Builders<FakeModel>.Filter.Empty));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => collection.FindOneAndReplaceAsync(Builders<FakeModel>.Filter.Empty, model));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => collection.FindOneAndUpdateAsync(
                Builders<FakeModel>.Filter.Empty,
                Builders<FakeModel>.Update.Set(m => m.IntegerProp, 1)));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => collection.InsertManyAsync([model]));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => collection.InsertOneAsync(model));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => collection.ReplaceOneAsync(Builders<FakeModel>.Filter.Empty, model));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => collection.UpdateManyAsync(
                Builders<FakeModel>.Filter.Empty,
                Builders<FakeModel>.Update.Set(m => m.IntegerProp, 1)));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => collection.UpdateOneAsync(
                Builders<FakeModel>.Filter.Empty,
                Builders<FakeModel>.Update.Set(m => m.IntegerProp, 1)));
            collectionMock.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task WritableCollectionAllowsWrites()
        {
            // Setup.
            var collection = new LimitedAccessMongoCollection<FakeModel>(engineMock.Object, collectionMock.Object, false);
            var model = new FakeModel();

            // Action.
            await collection.InsertOneAsync(model);
            await collection.Indexes.CreateOneAsync(
                new CreateIndexModel<FakeModel>(Builders<FakeModel>.IndexKeys.Ascending(m => m.IntegerProp)));

            // Assert.
            collectionMock.Verify(c => c.InsertOneAsync(model, It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once());
            indexManagerMock.Verify(m => m.CreateOneAsync(It.IsAny<CreateIndexModel<FakeModel>>(), It.IsAny<CreateOneIndexOptions>(), It.IsAny<CancellationToken>()), Times.Once());
        }
    }
}
