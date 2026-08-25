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
using Etherna.MongoDB.Bson.Serialization;
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
        private readonly InFlightOperationsCounter inFlightOperations = new();
        private readonly Mock<IMongoSearchIndexManager> searchIndexManagerMock = new();

        // Constructor.
        public LimitedAccessMongoCollectionTest()
        {
            //the registry resolves the output serializers of the rendered pipelines
            var serializerRegistry = new BsonSerializerRegistry();
            serializerRegistry.RegisterSerializationProvider(new BsonObjectModelSerializationProvider());

            collectionMock.Setup(c => c.DocumentSerializer)
                .Returns(new Mock<IBsonSerializer<FakeModel>>().Object);
            collectionMock.Setup(c => c.Indexes)
                .Returns(indexManagerMock.Object);
            collectionMock.Setup(c => c.SearchIndexes)
                .Returns(searchIndexManagerMock.Object);
            engineMock.Setup(e => e.ExecutionContext)
                .Returns(AsyncLocalContext.Instance);
            engineMock.Setup(e => e.SerializerRegistry)
                .Returns(serializerRegistry);
            //the guarded collections count their operations on the internal engine surface
            engineMock.As<IInternalDbContextEngine>()
                .Setup(e => e.InFlightOperations)
                .Returns(inFlightOperations);
        }

        // Tests.
        [Fact]
        public async Task DatabaseHandsOutGuardedCollections()
        {
            // Setup.
            /* An exclusive write lock is active on the engine, and the current flow holds
             * no exclusive access allowance. */
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            engineMock.Setup(e => e.IsExclusiveWriteEnabled)
                .Returns(true);
            var databaseMock = new Mock<IMongoDatabase>();
            var innerCollectionMock = new Mock<IMongoCollection<FakeModel>>();
            collectionMock.Setup(c => c.Database)
                .Returns(databaseMock.Object);
            databaseMock.Setup(d => d.GetCollection<FakeModel>("fakes", It.IsAny<MongoCollectionSettings>()))
                .Returns(innerCollectionMock.Object);
            var collection = new LimitedAccessMongoCollection<FakeModel>(engineMock.Object, collectionMock.Object, false);

            // Action and assert.
            var retrievedCollection = collection.Database.GetCollection<FakeModel>("fakes");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => retrievedCollection.InsertOneAsync(new FakeModel()));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => retrievedCollection.DeleteManyAsync(Builders<FakeModel>.Filter.Empty));
            innerCollectionMock.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ExclusiveWindowAdmissionsDoNotCountInFlight()
        {
            /* The exclusive access drain waits only for the operations admitted before the
             * window opened: a denied operation exits its count with its denial, and one
             * admitted by the allowance of its flow, meant to work during the window,
             * never counts. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            engineMock.Setup(e => e.IsExclusiveWriteEnabled)
                .Returns(true);
            var writeGate = new TaskCompletionSource();
            collectionMock.Setup(c => c.InsertOneAsync(It.IsAny<FakeModel>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
                .Returns(writeGate.Task);
            var collection = new LimitedAccessMongoCollection<FakeModel>(engineMock.Object, collectionMock.Object, false);

            // Action and assert.
            //denied without an allowance
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => collection.InsertOneAsync(new FakeModel()));
            Assert.Equal(0, inFlightOperations.WritesCount);

            //admitted by the allowance of this flow, and not counted while in flight
            using var allowance = new ExclusiveAccessHandler(engineMock.Object);
            var insertTask = collection.InsertOneAsync(new FakeModel());
            Assert.Equal(0, inFlightOperations.WritesCount);
            writeGate.SetResult();
            await insertTask;
            Assert.Equal(0, inFlightOperations.WritesCount);
        }

        [Fact]
        public async Task ForeignExclusiveAccessDeniesAggregateToCollectionPipelines()
        {
            // Setup.
            /* An exclusive write lock is active on the engine, and the current flow holds
             * no exclusive access allowance. */
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            engineMock.Setup(e => e.IsExclusiveWriteEnabled)
                .Returns(true);
            var collection = new LimitedAccessMongoCollection<FakeModel>(engineMock.Object, collectionMock.Object, false);
            var outPipeline = PipelineDefinition<FakeModel, BsonDocument>.Create(
                new BsonDocument("$out", "targetCollection"));

            // Action and assert.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => collection.AggregateAsync(outPipeline));
            collectionMock.VerifyGet(c => c.DocumentSerializer, Times.AtLeastOnce());
            collectionMock.VerifyNoOtherCalls();
        }

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
        public async Task ForeignExclusiveAccessDeniesMapReduceWithOutputCollection()
        {
            // Setup.
            /* An exclusive write lock is active on the engine, and the current flow holds
             * no exclusive access allowance. */
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            engineMock.Setup(e => e.IsExclusiveWriteEnabled)
                .Returns(true);
            var collection = new LimitedAccessMongoCollection<FakeModel>(engineMock.Object, collectionMock.Object, false);

            // Action and assert.
#pragma warning disable CS0618 //map reduce stays guarded while the driver exposes it
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => collection.MapReduceAsync(
                new BsonJavaScript("function() { emit(this._id, 1); }"),
                new BsonJavaScript("function(key, values) { return Array.sum(values); }"),
                new MapReduceOptions<FakeModel, BsonDocument>
                {
                    OutputOptions = MapReduceOutputOptions.Replace("targetCollection", "otherDb")
                }));
#pragma warning restore CS0618
            collectionMock.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task HandedOutWrappersCountTheirOperationsInFlight()
        {
            /* Index management and the database reached from a guarded collection keep its
             * guards: their operations count in flight on the engine like the collection
             * ones, so the exclusive access drain sees them too. */

            // Setup.
            var databaseMock = new Mock<IMongoDatabase>();
            collectionMock.Setup(c => c.Database)
                .Returns(databaseMock.Object);
            var indexGate = new TaskCompletionSource<string>();
            indexManagerMock.Setup(m => m.CreateOneAsync(It.IsAny<CreateIndexModel<FakeModel>>(), It.IsAny<CreateOneIndexOptions>(), It.IsAny<CancellationToken>()))
                .Returns(indexGate.Task);
            var listGate = new TaskCompletionSource<IAsyncCursor<string>>();
            databaseMock.Setup(d => d.ListCollectionNamesAsync(It.IsAny<ListCollectionNamesOptions>(), It.IsAny<CancellationToken>()))
                .Returns(listGate.Task);
            var collection = new LimitedAccessMongoCollection<FakeModel>(engineMock.Object, collectionMock.Object, false);

            // Action and assert.
            //an index write counts in flight until its completion
            var createIndexTask = collection.Indexes.CreateOneAsync(
                new CreateIndexModel<FakeModel>(Builders<FakeModel>.IndexKeys.Ascending(m => m.IntegerProp)));
            Assert.Equal(1, inFlightOperations.WritesCount);
            indexGate.SetResult("indexName");
            await createIndexTask;
            Assert.Equal(0, inFlightOperations.WritesCount);

            //a database read counts in flight until its completion
            var listNamesTask = collection.Database.ListCollectionNamesAsync();
            Assert.Equal(1, inFlightOperations.ReadsCount);
            listGate.SetResult(new Mock<IAsyncCursor<string>>().Object);
            await listNamesTask;
            Assert.Equal(0, inFlightOperations.ReadsCount);
        }

        [Fact]
        public async Task OperationsCountInFlightUntilTheirCompletion()
        {
            /* The exclusive access window drains the counted operations: an operation counts
             * from its admission to the completion of its forwarded call, a faulted one
             * included. */

            // Setup.
            var readGate = new TaskCompletionSource<IAsyncCursor<FakeModel>>();
            collectionMock.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<FakeModel>>(), It.IsAny<FindOptions<FakeModel, FakeModel>>(), It.IsAny<CancellationToken>()))
                .Returns(readGate.Task);
            collectionMock.Setup(c => c.InsertOneAsync(It.IsAny<FakeModel>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("fake driver failure"));
            var collection = new LimitedAccessMongoCollection<FakeModel>(engineMock.Object, collectionMock.Object, false);

            // Action and assert.
            //a read counts until its cursor is handed out
            var findTask = collection.FindAsync(Builders<FakeModel>.Filter.Empty);
            Assert.Equal(1, inFlightOperations.ReadsCount);
            readGate.SetResult(new Mock<IAsyncCursor<FakeModel>>().Object);
            await findTask;
            Assert.Equal(0, inFlightOperations.ReadsCount);

            //a faulted write exits its count with its fault
            await Assert.ThrowsAsync<InvalidOperationException>(() => collection.InsertOneAsync(new FakeModel()));
            Assert.Equal(0, inFlightOperations.WritesCount);
        }

        [Fact]
        public async Task ReadOnlyCollectionAllowsDatabaseReads()
        {
            // Setup.
            var databaseMock = new Mock<IMongoDatabase>();
            var innerCollectionMock = new Mock<IMongoCollection<FakeModel>>();
            collectionMock.Setup(c => c.Database)
                .Returns(databaseMock.Object);
            databaseMock.Setup(d => d.DatabaseNamespace)
                .Returns(new DatabaseNamespace("fakeDb"));
            databaseMock.Setup(d => d.GetCollection<FakeModel>("fakes", It.IsAny<MongoCollectionSettings>()))
                .Returns(innerCollectionMock.Object);
            var namesCursor = new Mock<IAsyncCursor<string>>().Object;
            databaseMock.Setup(d => d.ListCollectionNamesAsync(It.IsAny<ListCollectionNamesOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(namesCursor);
            var documentsCursor = new Mock<IAsyncCursor<FakeModel>>().Object;
            innerCollectionMock.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<FakeModel>>(), It.IsAny<FindOptions<FakeModel, FakeModel>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(documentsCursor);
            var collection = new LimitedAccessMongoCollection<FakeModel>(engineMock.Object, collectionMock.Object, true);

            // Action.
            var database = collection.Database;
            var databaseNamespace = database.DatabaseNamespace;
            var listedNames = await database.ListCollectionNamesAsync();
            var foundCursor = await database.GetCollection<FakeModel>("fakes").FindAsync(Builders<FakeModel>.Filter.Empty);

            // Assert.
            Assert.Equal("fakeDb", databaseNamespace.DatabaseName);
            Assert.Same(namesCursor, listedNames);
            Assert.Same(documentsCursor, foundCursor);
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
        public async Task ReadOnlyCollectionAllowsInlineMapReduce()
        {
            // Setup.
            var collection = new LimitedAccessMongoCollection<FakeModel>(engineMock.Object, collectionMock.Object, true);
            var map = new BsonJavaScript("function() { emit(this._id, 1); }");
            var reduce = new BsonJavaScript("function(key, values) { return Array.sum(values); }");
            var cursor = new Mock<IAsyncCursor<BsonDocument>>().Object;
#pragma warning disable CS0618 //map reduce stays guarded while the driver exposes it
            collectionMock.Setup(c => c.MapReduceAsync(map, reduce, It.IsAny<MapReduceOptions<FakeModel, BsonDocument>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursor);

            // Action.
            var defaultOutputCursor = await collection.MapReduceAsync<BsonDocument>(map, reduce);
            var inlineOutputCursor = await collection.MapReduceAsync(map, reduce,
                new MapReduceOptions<FakeModel, BsonDocument> { OutputOptions = MapReduceOutputOptions.Inline });
#pragma warning restore CS0618

            // Assert.
            Assert.Same(cursor, defaultOutputCursor);
            Assert.Same(cursor, inlineOutputCursor);
        }

        [Fact]
        public async Task ReadOnlyCollectionAllowsReadAggregates()
        {
            // Setup.
            var collection = new LimitedAccessMongoCollection<FakeModel>(engineMock.Object, collectionMock.Object, true);
            var pipeline = PipelineDefinition<FakeModel, BsonDocument>.Create(
                new BsonDocument("$match", new BsonDocument()));
            var cursor = new Mock<IAsyncCursor<BsonDocument>>().Object;
            collectionMock.Setup(c => c.AggregateAsync(pipeline, It.IsAny<AggregateOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursor);

            // Action.
            var aggregateCursor = await collection.AggregateAsync(pipeline);

            // Assert.
            Assert.Same(cursor, aggregateCursor);
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
        public async Task ReadOnlyCollectionAllowsReadsThroughOfType()
        {
            // Setup.
            var filteredCollectionMock = new Mock<IFilteredMongoCollection<FakeModelWithExtraElements>>();
            collectionMock.Setup(c => c.OfType<FakeModelWithExtraElements>())
                .Returns(filteredCollectionMock.Object);
            var filter = Builders<FakeModelWithExtraElements>.Filter.Empty;
            filteredCollectionMock.Setup(c => c.Filter)
                .Returns(filter);
            var cursor = new Mock<IAsyncCursor<FakeModelWithExtraElements>>().Object;
            filteredCollectionMock.Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<FakeModelWithExtraElements>>(),
                    It.IsAny<FindOptions<FakeModelWithExtraElements, FakeModelWithExtraElements>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursor);
            var collection = new LimitedAccessMongoCollection<FakeModel>(engineMock.Object, collectionMock.Object, true);

            // Action.
            var filteredCollection = collection.OfType<FakeModelWithExtraElements>();
            var filterDefinition = filteredCollection.Filter;
            var foundCursor = await filteredCollection.FindAsync(Builders<FakeModelWithExtraElements>.Filter.Empty);

            // Assert.
            Assert.Same(filter, filterDefinition);
            Assert.Same(cursor, foundCursor);
        }

        [Fact]
        public async Task ReadOnlyCollectionDeniesAggregateToCollectionPipelines()
        {
            // Setup.
            var collection = new LimitedAccessMongoCollection<FakeModel>(engineMock.Object, collectionMock.Object, true);
            var outPipeline = PipelineDefinition<FakeModel, BsonDocument>.Create(
                new BsonDocument("$match", new BsonDocument()),
                new BsonDocument("$out", "targetCollection"));
            var mergePipeline = PipelineDefinition<FakeModel, BsonDocument>.Create(
                new BsonDocument("$merge", new BsonDocument("into", new BsonDocument
                {
                    ["db"] = "otherDb",
                    ["coll"] = "targetCollection"
                })));

            // Action and assert.
            Assert.Throws<UnauthorizedAccessException>(() => collection.Aggregate(outPipeline));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => collection.AggregateAsync(mergePipeline));
            collectionMock.VerifyGet(c => c.DocumentSerializer, Times.AtLeastOnce());
            collectionMock.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ReadOnlyCollectionDeniesDatabaseLevelWrites()
        {
            // Setup.
            var databaseMock = new Mock<IMongoDatabase>();
            collectionMock.Setup(c => c.Database)
                .Returns(databaseMock.Object);
            var collection = new LimitedAccessMongoCollection<FakeModel>(engineMock.Object, collectionMock.Object, true);

            // Action and assert.
            var database = collection.Database;
            var exception = Assert.Throws<UnauthorizedAccessException>(() => database.DropCollection("fakes"));
            Assert.Equal("Database is read only", exception.Message);
            Assert.Throws<UnauthorizedAccessException>(() => database.AggregateToCollection(new EmptyPipelineDefinition<NoPipelineInput>()));
            //a database level aggregate writing into a collection is a write, like at collection level
            var outPipeline = PipelineDefinition<NoPipelineInput, BsonDocument>.Create(
                new BsonDocument("$out", "targetCollection"));
            Assert.Throws<UnauthorizedAccessException>(() => database.Aggregate(outPipeline));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => database.AggregateAsync(outPipeline));
            Assert.Throws<UnauthorizedAccessException>(() => database.CreateCollection("denied"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => database.CreateCollectionAsync("denied"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => database.CreateViewAsync(
                "deniedView", "fakes", new EmptyPipelineDefinition<FakeModel>()));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => database.DropCollectionAsync("fakes"));
            Assert.Throws<UnauthorizedAccessException>(() => database.RenameCollection("fakes", "renamedFakes"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => database.RenameCollectionAsync("fakes", "renamedFakes"));
            Assert.Throws<UnauthorizedAccessException>(() => database.RunCommand(
                new BsonDocumentCommand<BsonDocument>(new BsonDocument("dropDatabase", 1))));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => database.RunCommandAsync(
                new BsonDocumentCommand<BsonDocument>(new BsonDocument("dropDatabase", 1))));
            databaseMock.VerifyNoOtherCalls();
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
        public async Task ReadOnlyCollectionDeniesMapReduceWithOutputCollection()
        {
            // Setup.
            var collection = new LimitedAccessMongoCollection<FakeModel>(engineMock.Object, collectionMock.Object, true);
            var map = new BsonJavaScript("function() { emit(this._id, 1); }");
            var reduce = new BsonJavaScript("function(key, values) { return Array.sum(values); }");

            // Action and assert.
#pragma warning disable CS0618 //map reduce stays guarded while the driver exposes it
            Assert.Throws<UnauthorizedAccessException>(() => collection.MapReduce(map, reduce,
                new MapReduceOptions<FakeModel, BsonDocument> { OutputOptions = MapReduceOutputOptions.Replace("targetCollection", "otherDb") }));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => collection.MapReduceAsync(map, reduce,
                new MapReduceOptions<FakeModel, BsonDocument> { OutputOptions = MapReduceOutputOptions.Merge("targetCollection", "otherDb") }));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => collection.MapReduceAsync(map, reduce,
                new MapReduceOptions<FakeModel, BsonDocument> { OutputOptions = MapReduceOutputOptions.Reduce("targetCollection", "otherDb") }));
#pragma warning restore CS0618
            collectionMock.VerifyNoOtherCalls();
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
        public async Task ReadOnlyCollectionDeniesWritesThroughDatabaseCollections()
        {
            // Setup.
            var databaseMock = new Mock<IMongoDatabase>();
            var innerCollectionMock = new Mock<IMongoCollection<FakeModel>>();
            collectionMock.Setup(c => c.Database)
                .Returns(databaseMock.Object);
            databaseMock.Setup(d => d.GetCollection<FakeModel>("fakes", It.IsAny<MongoCollectionSettings>()))
                .Returns(innerCollectionMock.Object);
            var collection = new LimitedAccessMongoCollection<FakeModel>(engineMock.Object, collectionMock.Object, true);

            // Action and assert.
            var retrievedCollection = collection.Database.GetCollection<FakeModel>("fakes");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => retrievedCollection.DeleteManyAsync(Builders<FakeModel>.Filter.Empty));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => retrievedCollection.InsertOneAsync(new FakeModel()));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => retrievedCollection.ReplaceOneAsync(
                Builders<FakeModel>.Filter.Empty, new FakeModel()));
            innerCollectionMock.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ReadOnlyCollectionDeniesWritesThroughOfType()
        {
            // Setup.
            var filteredCollectionMock = new Mock<IFilteredMongoCollection<FakeModelWithExtraElements>>();
            collectionMock.Setup(c => c.OfType<FakeModelWithExtraElements>())
                .Returns(filteredCollectionMock.Object);
            var collection = new LimitedAccessMongoCollection<FakeModel>(engineMock.Object, collectionMock.Object, true);

            // Action and assert.
            var filteredCollection = collection.OfType<FakeModelWithExtraElements>();
            var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                filteredCollection.DeleteManyAsync(Builders<FakeModelWithExtraElements>.Filter.Empty));
            Assert.Equal("Collection is read only", exception.Message);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                filteredCollection.InsertOneAsync(new FakeModelWithExtraElements()));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => filteredCollection.UpdateManyAsync(
                Builders<FakeModelWithExtraElements>.Filter.Empty,
                Builders<FakeModelWithExtraElements>.Update.Set(m => m.IntegerProp, 1)));
            filteredCollectionMock.VerifyNoOtherCalls();
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
        [Fact]
        public async Task WritableCollectionAllowsWritesThroughDatabaseAndOfType()
        {
            // Setup.
            var databaseMock = new Mock<IMongoDatabase>();
            var filteredCollectionMock = new Mock<IFilteredMongoCollection<FakeModelWithExtraElements>>();
            var innerCollectionMock = new Mock<IMongoCollection<FakeModel>>();
            collectionMock.Setup(c => c.Database)
                .Returns(databaseMock.Object);
            collectionMock.Setup(c => c.OfType<FakeModelWithExtraElements>())
                .Returns(filteredCollectionMock.Object);
            databaseMock.Setup(d => d.GetCollection<FakeModel>("fakes", It.IsAny<MongoCollectionSettings>()))
                .Returns(innerCollectionMock.Object);
            databaseMock.Setup(d => d.RunCommandAsync(
                    It.IsAny<Command<BsonDocument>>(),
                    It.IsAny<ReadPreference>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BsonDocument());
            var collection = new LimitedAccessMongoCollection<FakeModel>(engineMock.Object, collectionMock.Object, false);
            var model = new FakeModel();

            // Action.
            await collection.Database.GetCollection<FakeModel>("fakes").InsertOneAsync(model);
            await collection.Database.RunCommandAsync(new BsonDocumentCommand<BsonDocument>(new BsonDocument("collStats", "fakes")));
            await collection.OfType<FakeModelWithExtraElements>().DeleteManyAsync(Builders<FakeModelWithExtraElements>.Filter.Empty);

            // Assert.
            innerCollectionMock.Verify(c => c.InsertOneAsync(model, It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once());
            databaseMock.Verify(d => d.RunCommandAsync(
                It.IsAny<Command<BsonDocument>>(),
                It.IsAny<ReadPreference>(),
                It.IsAny<CancellationToken>()), Times.Once());
            filteredCollectionMock.Verify(c => c.DeleteManyAsync(
                It.IsAny<FilterDefinition<FakeModelWithExtraElements>>(),
                It.IsAny<DeleteOptions>(),
                It.IsAny<CancellationToken>()), Times.Once());
        }

    }
}
