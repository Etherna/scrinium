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
using Etherna.MongoDB.Driver.Core.Clusters;
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Etherna.Scrinium.Core.Models;
using Etherna.Scrinium.Core.Exceptions;
using Etherna.Scrinium.Core.Options;
using Etherna.Scrinium.Core.ProxyModels;
using Etherna.Scrinium.Core.Repositories;
using Etherna.Scrinium.Core.Serialization.Mapping;
using Etherna.Scrinium.Core.Utility;
using Moq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.Core
{
    public class DbContextTest : IDisposable
    {
        // Consts.
        //bounds the handovers between the flows of a test: a missing signal must fail it, not hang it
        private static readonly TimeSpan HandoverBound = TimeSpan.FromSeconds(30);
        //bounds the seeding lock waits: a regressed wait resolution must fail the tests, not hang them
        private static readonly TimeSpan SeedingWaitBound = TimeSpan.FromSeconds(30);

        // Fields.
        private readonly FakeDbContext dbContext;
        private readonly IDbContextEngine engine;
        //the internal facing surface of the db context under test, explicitly implemented
        private readonly IInternalDbContext internalDbContext;

        private readonly Mock<ICluster> clusterMock = new();
        private readonly Mock<IMongoCollection<FakeModel>> collectionMock = new();
        private readonly Mock<IDbDependencies> dependenciesMock = new();
        private readonly Mock<IMapRegistry> mapRegistryMock = new();
        private readonly Mock<IMongoClient> mongoClientMock = new();
        private readonly Mock<IMongoDatabase> mongoDatabaseMock = new();
        private readonly DbContextOptions options = new();
        private readonly Mock<IProxyGenerator> proxyGeneratorMock = new();
        
        // Constructor.
        public DbContextTest()
        {
            dependenciesMock.Setup(d => d.BsonSerializerRegistry)
                .Returns(new BsonSerializerRegistry());
            dependenciesMock.Setup(d => d.DbMaintainer)
                .Returns(new Mock<IDbMaintainer>().Object);
            dependenciesMock.Setup(d => d.DbMigrationManager)
                .Returns(new Mock<IDbMigrationManager>().Object);
            dependenciesMock.Setup(d => d.DiscriminatorRegistry)
                .Returns(new Mock<IDiscriminatorRegistry>().Object);
            dependenciesMock.Setup(d => d.ExecutionContext)
                .Returns(AsyncLocalContext.Instance);
            /* Resolve the mapped id member of the fake models from a real frozen class
             * map: the db context reads the identity always from the mapping. The class
             * map is built on the level declaring the id member. */
            var fakeModelClassMap = new BsonClassMap<FakeEntityModelBase<string>>(cm =>
                cm.MapIdMember(m => m.Id));
            fakeModelClassMap.Freeze();
            var modelMapSchemaMock = new Mock<IModelMapSchema>();
            modelMapSchemaMock.Setup(s => s.AllMemberMaps)
                .Returns([fakeModelClassMap.IdMemberMap!]);
            var modelMapMock = new Mock<IModelMap>();
            modelMapMock.Setup(m => m.ActiveSchema)
                .Returns(modelMapSchemaMock.Object);
            var modelMap = modelMapMock.Object;
            mapRegistryMock.Setup(r => r.TryGetModelMap(It.IsAny<Type>(), out modelMap))
                .Returns(true);
            dependenciesMock.Setup(d => d.MapRegistry)
                .Returns(mapRegistryMock.Object);
            proxyGeneratorMock.Setup(p => p.PurgeProxyType(It.IsAny<Type>()))
                .Returns<Type>(t => t);
            dependenciesMock.Setup(d => d.ProxyGenerator)
                .Returns(proxyGeneratorMock.Object);
            dependenciesMock.Setup(d => d.RepositoryRegistry)
                .Returns(() => new RepositoryRegistry());

            var cursorMock = new Mock<IAsyncCursor<FakeModel>>();
            cursorMock.Setup(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            cursorMock.SetupGet(c => c.Current)
                .Returns([new FakeModel { Id = "id" }]);
            
            collectionMock.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<FakeModel>>(), It.IsAny<FindOptions<FakeModel, FakeModel>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            mongoDatabaseMock.Setup(d => d.GetCollection<FakeModel>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()))
                .Returns(collectionMock.Object);
            
            mongoClientMock.Setup(c => c.GetDatabase(It.IsAny<string>(), It.IsAny<MongoDatabaseSettings>()))
                .Returns(mongoDatabaseMock.Object);

            //standalone topology by default: no implicit transactions
            clusterMock.Setup(c => c.Description)
                .Returns(NewClusterDescription(ClusterType.Standalone));
            mongoClientMock.Setup(c => c.Cluster)
                .Returns(clusterMock.Object);

            dbContext = new FakeDbContext();
            internalDbContext = dbContext;
            engine = dbContext.BuildEngine(
                dependenciesMock.Object,
                mongoClientMock.Object,
                options);
            dbContext.AttachToEngine(engine, [], dependenciesMock.Object.RepositoryRegistry);
        }

        // Dispose.
        public void Dispose()
        {
            (engine as IDisposable)?.Dispose();
            GC.SuppressFinalize(this);
        }
        
        // Tests.
        [Fact]
        public void OnImplicitLazyLoadDeniesLoadsWithThrowMode()
        {
            // Setup.
            options.ImplicitLazyLoad = ReactionMode.Throw;

            // Action and assert.
            var exception = Assert.Throws<ScriniumLazyLoadingException>(
                () => internalDbContext.OnImplicitLazyLoad(typeof(FakeModel), "StringProp"));
            Assert.Contains(nameof(FakeModel), exception.Message, StringComparison.Ordinal);
            Assert.Contains("StringProp", exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(ReactionMode.Silent)]
        [InlineData(ReactionMode.Warn)]
        public void OnImplicitLazyLoadAllowsLoadsWithNotThrowingModes(ReactionMode mode)
        {
            // Setup.
            options.ImplicitLazyLoad = mode;

            // Action, asserting no throw.
            internalDbContext.OnImplicitLazyLoad(typeof(FakeModel), "StringProp");
            internalDbContext.OnImplicitLazyLoad(typeof(FakeModel), "StringProp"); //repeated: warn dedups per scope
        }

        [Fact]
        public void OnMissingOriginDocumentDeniesSummariesWithThrowMode()
        {
            // Setup.
            var model = NewBoundProxy("id");
            ((IReferenceable)model).SetAsSummary([], ReactionMode.Throw);

            // Action and assert.
            var exception = Assert.Throws<ScriniumMissingOriginDocumentException>(
                () => internalDbContext.OnMissingOriginDocument(model));
            Assert.Contains(nameof(FakeModel), exception.Message, StringComparison.Ordinal);
            Assert.Contains("id", exception.Message, StringComparison.Ordinal);
            Assert.Contains(dbContext.FakeModels.Name, exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(ReactionMode.Silent)]
        [InlineData(ReactionMode.Warn)]
        public void OnMissingOriginDocumentToleratesSummariesWithNotThrowingModes(ReactionMode mode)
        {
            // Setup.
            var model = NewBoundProxy("id");
            ((IReferenceable)model).SetAsSummary([], mode);

            // Action, asserting no throw.
            internalDbContext.OnMissingOriginDocument(model);
            internalDbContext.OnMissingOriginDocument(model); //repeated: warn dedups per scope
        }

        [Fact]
        public async Task PreloadReportsMissingOriginDocuments()
        {
            /* A summary still requiring its members after the preload found no origin
             * document: the explicit load reports the db inconsistency like an implicit one,
             * instead of leaving the model summary until its first member read. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            var model = NewBoundProxy("id");
            ((IReferenceable)model).SetAsSummary([], ReactionMode.Throw);

            collectionMock.Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<FakeModel>>(),
                    It.IsAny<FindOptions<FakeModel, FakeModel>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(NewEmptyCursor);

            // Action and assert.
            var exception = await Assert.ThrowsAsync<ScriniumMissingOriginDocumentException>(
                () => dbContext.LoadValuesAsync(model, m => m.StringProp));
            Assert.Contains("id", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ExecuteInTransactionCommitsAndEnlistsOperations()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            var sessionMock = new Mock<IClientSessionHandle>();
            mongoClientMock.Setup(c => c.StartSessionAsync(It.IsAny<ClientSessionOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(sessionMock.Object);

            var cursorMock = new Mock<IAsyncCursor<FakeModel>>();
            cursorMock.Setup(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            cursorMock.SetupGet(c => c.Current)
                .Returns([new FakeModel { Id = "id" }]);
            collectionMock.Setup(c => c.FindAsync(sessionMock.Object, It.IsAny<FilterDefinition<FakeModel>>(), It.IsAny<FindOptions<FakeModel, FakeModel>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            // Action.
            var result = await dbContext.ExecuteInTransactionAsync(async () =>
            {
                //the session is registered as ambient for the engine
                Assert.Same(sessionMock.Object, DbSessionHandler.TryGetCurrentSession(engine));

                //operations invoked without an explicit session enlist in the ambient one
                await dbContext.FakeModels.CreateAsync(new FakeModel { Id = "id" });
                await dbContext.FakeModels.FindOneAsync("id");

                return 42;
            });

            // Assert.
            Assert.Equal(42, result);
            Assert.Null(DbSessionHandler.TryGetCurrentSession(engine));

            sessionMock.Verify(s => s.StartTransaction(It.IsAny<TransactionOptions>()), Times.Once);
            sessionMock.Verify(s => s.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
            sessionMock.Verify(s => s.AbortTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
            collectionMock.Verify(c => c.InsertOneAsync(sessionMock.Object, It.IsAny<FakeModel>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once);
            collectionMock.Verify(c => c.FindAsync(sessionMock.Object, It.IsAny<FilterDefinition<FakeModel>>(), It.IsAny<FindOptions<FakeModel, FakeModel>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteInTransactionAbortsOnException()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            var sessionMock = new Mock<IClientSessionHandle>();
            mongoClientMock.Setup(c => c.StartSessionAsync(It.IsAny<ClientSessionOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(sessionMock.Object);

            // Action.
            await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.ExecuteInTransactionAsync(
                () => Task.FromException(new InvalidOperationException())));

            // Assert.
            Assert.Null(DbSessionHandler.TryGetCurrentSession(engine));

            sessionMock.Verify(s => s.StartTransaction(It.IsAny<TransactionOptions>()), Times.Once);
            sessionMock.Verify(s => s.AbortTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
            sessionMock.Verify(s => s.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task SaveChangesRunsIntoTransactionOnReplicaSet()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            SetReplicaSetTopology();

            var sessionMock = new Mock<IClientSessionHandle>();
            mongoClientMock.Setup(c => c.StartSessionAsync(It.IsAny<ClientSessionOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(sessionMock.Object);

            var repositoryMock = NewSourceRepositoryMock();
            IClientSessionHandle? sessionDuringSave = null;
            repositoryMock.Setup(r => r.SaveChangesAsync(It.IsAny<IEntityModel>(), It.IsAny<CancellationToken>()))
                .Callback(() => sessionDuringSave = DbSessionHandler.TryGetCurrentSession(engine))
                .Returns(Task.CompletedTask);

            var modelMock = NewChangedModelMock(repositoryMock.Object);
            MarkModelChanged(dbContext, modelMock.Object);

            // Action.
            await dbContext.SaveChangesAsync();

            // Assert.
            //the model saved with the implicit transaction session as ambient
            Assert.Same(sessionMock.Object, sessionDuringSave);
            sessionMock.Verify(s => s.StartTransaction(It.IsAny<TransactionOptions>()), Times.Once);
            sessionMock.Verify(s => s.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
            repositoryMock.Verify(r => r.SaveChangesAsync(modelMock.Object, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task TransactionalSaveChangesEnlistsInAmbientSession()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            SetReplicaSetTopology();

            var sessionMock = new Mock<IClientSessionHandle>();
            mongoClientMock.Setup(c => c.StartSessionAsync(It.IsAny<ClientSessionOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(sessionMock.Object);

            var repositoryMock = NewSourceRepositoryMock();
            IClientSessionHandle? sessionDuringSave = null;
            repositoryMock.Setup(r => r.SaveChangesAsync(It.IsAny<IEntityModel>(), It.IsAny<CancellationToken>()))
                .Callback(() => sessionDuringSave = DbSessionHandler.TryGetCurrentSession(engine))
                .Returns(Task.CompletedTask);

            var modelMock = NewChangedModelMock(repositoryMock.Object);
            MarkModelChanged(dbContext, modelMock.Object);

            // Action.
            await dbContext.ExecuteInTransactionAsync(
                () => dbContext.SaveChangesAsync());

            // Assert.
            //a single session started: the save enlisted in the ambient transaction without nesting
            Assert.Same(sessionMock.Object, sessionDuringSave);
            mongoClientMock.Verify(c => c.StartSessionAsync(It.IsAny<ClientSessionOptions>(), It.IsAny<CancellationToken>()), Times.Once);
            sessionMock.Verify(s => s.StartTransaction(It.IsAny<TransactionOptions>()), Times.Once);
            sessionMock.Verify(s => s.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SaveChangesSkipsTransactionOnStandalone()
        {
            // Setup.
            //the default mocked topology is standalone
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            var repositoryMock = NewSourceRepositoryMock();
            var modelMock = NewChangedModelMock(repositoryMock.Object);
            MarkModelChanged(dbContext, modelMock.Object);

            // Action.
            await dbContext.SaveChangesAsync();

            // Assert.
            repositoryMock.Verify(r => r.SaveChangesAsync(modelMock.Object, It.IsAny<CancellationToken>()), Times.Once);
            mongoClientMock.Verify(c => c.StartSessionAsync(It.IsAny<ClientSessionOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task SaveChangesSkipsTransactionWhenDisabledByOptions()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            SetReplicaSetTopology();

            var noTransactionsDbContext = BuildDbContext(
                new DbContextOptions { EnableTransactionsWithReplicaSet = false },
                out var noTransactionsEngine);

            var repositoryMock = NewSourceRepositoryMock();
            var modelMock = NewChangedModelMock(repositoryMock.Object);
            MarkModelChanged(noTransactionsDbContext, modelMock.Object);

            // Action.
            await noTransactionsDbContext.SaveChangesAsync();

            // Assert.
            repositoryMock.Verify(r => r.SaveChangesAsync(modelMock.Object, It.IsAny<CancellationToken>()), Times.Once);
            mongoClientMock.Verify(c => c.StartSessionAsync(It.IsAny<ClientSessionOptions>(), It.IsAny<CancellationToken>()), Times.Never);

            (noTransactionsEngine as IDisposable)?.Dispose();
        }

        [Fact]
        public void LoadedModelsAreRegisteredPerInstance()
        {
            // Setup.
            var model = new FakeModel { Id = "id" };

            // Action.
            internalDbContext.RegisterLoadedModel("id", model);

            // Assert.
            Assert.Same(model, dbContext.TryGetLoadedModel(dbContext.FakeModels, "id"));
            Assert.Null(dbContext.TryGetLoadedModel(dbContext.FakeModels, "otherId"));

            // Action.
            dbContext.UnregisterLoadedModel("id", model);

            // Assert.
            Assert.Null(dbContext.TryGetLoadedModel(dbContext.FakeModels, "id"));
        }

        [Fact]
        public void ReplaceOutdatedLoadedModelSwapsTheLoadedInstance()
        {
            // Setup.
            var outdatedModel = NewBoundProxy("id");
            var currentModel = NewBoundProxy("id");
            internalDbContext.RegisterLoadedModel("id", outdatedModel);

            // Action.
            internalDbContext.ReplaceOutdatedLoadedModel("id", outdatedModel, currentModel);

            // Assert.
            //the fresh instance becomes the loaded one, and only the outdated one is flagged
            Assert.Same(currentModel, dbContext.TryGetLoadedModel(dbContext.FakeModels, "id"));
            Assert.True(dbContext.IsOutdatedModel(outdatedModel));
            Assert.False(dbContext.IsOutdatedModel(currentModel));
        }

        [Fact]
        public void ReplaceOutdatedLoadedModelValidatesTheModelIds()
        {
            // Setup.
            var outdatedModel = NewBoundProxy("id");
            var currentModel = NewBoundProxy("otherId");
            internalDbContext.RegisterLoadedModel("id", outdatedModel);

            // Action.
            var exception = Assert.Throws<ArgumentException>(
                () => internalDbContext.ReplaceOutdatedLoadedModel("id", outdatedModel, currentModel));

            // Assert.
            //the mismatch fails fast, before any state mutation
            Assert.Equal("currentModel", exception.ParamName);
            Assert.False(dbContext.IsOutdatedModel(outdatedModel));
            Assert.Same(outdatedModel, dbContext.TryGetLoadedModel(dbContext.FakeModels, "id"));
        }

        [Fact]
        public void TransientModelsScopeEvictsModelsEnteredInside()
        {
            // Setup.
            var model = NewBoundProxy("id");

            // Action.
            using (dbContext.StartTransientModelsScope())
            {
                internalDbContext.RegisterLoadedModel("id", model);
                internalDbContext.SetModelBsonDocument(model, []);
                internalDbContext.MarkChangeCandidate(model);

                //inside the scope the model loads and tracks normally
                Assert.Same(model, dbContext.TryGetLoadedModel(dbContext.FakeModels, "id"));
                Assert.NotNull(internalDbContext.TryGetModelBsonDocument(model));
                Assert.Contains(model, dbContext.ChangedModelsList);
            }

            // Assert.
            //the scope end evicted the model from the loaded models and the change tracking
            Assert.Null(dbContext.TryGetLoadedModel(dbContext.FakeModels, "id"));
            Assert.Null(internalDbContext.TryGetModelBsonDocument(model));
            Assert.Empty(dbContext.ChangedModelsList);
        }

        [Fact]
        public void TransientModelsScopeKeepsModelsEnteredBefore()
        {
            // Setup.
            var model = NewBoundProxy("id");
            internalDbContext.RegisterLoadedModel("id", model);
            internalDbContext.SetModelBsonDocument(model, []);

            // Action.
            var updatedDocument = new BsonDocument("updated", true);
            using (dbContext.StartTransientModelsScope())
            {
                internalDbContext.SetModelBsonDocument(model, updatedDocument);
                internalDbContext.MarkChangeCandidate(model);
            }

            // Assert.
            //the model entered before the scope keeps its state, updates inside the scope included
            Assert.Same(model, dbContext.TryGetLoadedModel(dbContext.FakeModels, "id"));
            Assert.Same(updatedDocument, internalDbContext.TryGetModelBsonDocument(model));
            Assert.Contains(model, dbContext.ChangedModelsList);
        }

        [Fact]
        public async Task BatchPreloadChunksTheLoadQueries()
        {
            /* One summary more than a chunk: the batch preload must split the ids into a
             * full chunk query and a remainder query, instead of a single unbounded $in
             * filter growing with the caller batch size. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            const int loadChunkSize = 1000; //keep aligned with the repository load chunk size
            var models = Enumerable.Range(0, loadChunkSize + 1)
                .Select(i =>
                {
                    var proxy = NewBoundProxy($"id{i}");
                    //the mocked collection returns no document: the missing origin ones are not the object here
                    ((IReferenceable)proxy).SetAsSummary([], ReactionMode.Silent);
                    return proxy;
                })
                .ToArray();

            List<FilterDefinition<FakeModel>> capturedFilters = [];
            collectionMock.Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<FakeModel>>(),
                    It.IsAny<FindOptions<FakeModel, FakeModel>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<FakeModel>, FindOptions<FakeModel, FakeModel>, CancellationToken>(
                    (filter, _, _) => capturedFilters.Add(filter))
                .ReturnsAsync(NewEmptyCursor);

            // Action.
            await dbContext.LoadValuesAsync(models, m => m.StringProp);

            // Assert.
            //one query per ids chunk, together covering every model id
            var queriedIdsGroups = capturedFilters.Select(RenderedInIds).ToArray();
            Assert.Equal([loadChunkSize, 1], queriedIdsGroups.Select(ids => ids.Length));
            Assert.Equal(
                models.Select(m => m.Id).ToHashSet(),
                queriedIdsGroups.SelectMany(ids => ids).ToHashSet());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CanRunExclusiveAccess(bool lockOnRead)
        {
            /* The two flows hand over with signals, and not with delays: an exclusive window
             * that opens late, on a loaded machine, would otherwise still be open when the
             * other flow expects it closed. */
            var fakeModel = new FakeModel { Id = "id" };
            var deniedAccessObserved = new TaskCompletionSource();
            var exclusiveAccessEnded = new TaskCompletionSource();
            var exclusiveAccessStarted = new TaskCompletionSource();
            var freeAccessObserved = new TaskCompletionSource();

            async Task Process1()
            {
                using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

                //succeeds without exclusive access
                await dbContext.FakeModels.CreateAsync(fakeModel);
                await dbContext.FakeModels.FindOneAsync("test");
                freeAccessObserved.SetResult();

                //fails with exclusive access without an allowed area. Can read if not locked
                await exclusiveAccessStarted.Task.WaitAsync(HandoverBound);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => dbContext.FakeModels.CreateAsync(fakeModel));
                if (lockOnRead)
                    await Assert.ThrowsAsync<UnauthorizedAccessException>(() => dbContext.FakeModels.FindOneAsync("test"));
                else
                    await dbContext.FakeModels.FindOneAsync("test");
                deniedAccessObserved.SetResult();

                //succeeds without exclusive access
                await exclusiveAccessEnded.Task.WaitAsync(HandoverBound);
                await dbContext.FakeModels.CreateAsync(fakeModel);
                await dbContext.FakeModels.FindOneAsync("test");
            }
            async Task Process2()
            {
                using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

                //succeeds without exclusive access
                await dbContext.FakeModels.CreateAsync(fakeModel);
                await dbContext.FakeModels.FindOneAsync("test");

                //run exclusive access with allowed area, entered after the other flow accessed freely
                await freeAccessObserved.Task.WaitAsync(HandoverBound);
                var result = await dbContext.Engine.RunWithExclusiveAccessAsync(async () =>
                {
                    //succeed with exclusive access in allowed area
                    await dbContext.FakeModels.CreateAsync(fakeModel);
                    await dbContext.FakeModels.FindOneAsync("test");
                    exclusiveAccessStarted.SetResult();

                    //hold the window open until the other flow observed its denial
                    await deniedAccessObserved.Task.WaitAsync(HandoverBound);

                    return 42;
                }, lockOnRead);
                Assert.Equal(42, result);
                exclusiveAccessEnded.SetResult();

                //succeeds without exclusive access
                await dbContext.FakeModels.CreateAsync(fakeModel);
                await dbContext.FakeModels.FindOneAsync("test");
            }

            await Task.WhenAll(Process1(), Process2());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExclusiveAccessDrainsTheInFlightOperations(bool isWriteOperation)
        {
            /* An operation admitted a moment before the window opens keeps running against
             * the collection: the window completes it before starting the exclusive work,
             * instead of running beside it. */

            // Setup.
            var operationGate = new TaskCompletionSource();
            if (isWriteOperation)
                collectionMock.Setup(c => c.InsertOneAsync(It.IsAny<FakeModel>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
                    .Returns(operationGate.Task);
            else
                collectionMock.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<FakeModel>>(), It.IsAny<FindOptions<FakeModel, FakeModel>>(), It.IsAny<CancellationToken>()))
                    .Returns(async () =>
                    {
                        await operationGate.Task;
                        return NewEmptyCursor();
                    });
            var collection = engine.GetMongoCollection<FakeModel>("fakeModels");
            var inFlightOperations = ((IInternalDbContextEngine)engine).InFlightOperations;
            var operationStarted = new TaskCompletionSource();

            async Task OperationFlow()
            {
                using var flowContext = AsyncLocalContext.Instance.InitAsyncLocalContext();

                //admitted before the window: the operation stays in flight on the gate
                Task operationTask = isWriteOperation
                    ? collection.InsertOneAsync(new FakeModel())
                    : collection.FindAsync(Builders<FakeModel>.Filter.Empty);
                operationStarted.SetResult();
                await operationTask;
            }
            async Task WindowFlow()
            {
                using var flowContext = AsyncLocalContext.Instance.InitAsyncLocalContext();

                await operationStarted.Task.WaitAsync(HandoverBound);
                await engine.RunWithExclusiveAccessAsync(() =>
                {
                    //the drain left no admitted operation in flight beside the exclusive work
                    Assert.Equal(0, isWriteOperation
                        ? inFlightOperations.WritesCount
                        : inFlightOperations.ReadsCount);
                    return Task.CompletedTask;
                });
            }

            // Action.
            var operationFlowTask = OperationFlow();
            var windowFlowTask = WindowFlow();

            //release the gate only once the window is open, so the drain really waits
            await operationStarted.Task.WaitAsync(HandoverBound);
            var flagsStopwatch = Stopwatch.StartNew();
            while (!engine.IsExclusiveWriteEnabled)
            {
                //a window already closed can only have run beside the operation: surface its assert
                if (windowFlowTask.IsCompleted)
                {
                    await windowFlowTask;
                    Assert.Fail("The window closed while the admitted operation was still in flight");
                }
                Assert.True(flagsStopwatch.Elapsed < HandoverBound);
                await Task.Delay(1);
            }
            Assert.False(windowFlowTask.IsCompleted);
            operationGate.SetResult();
            await Task.WhenAll(operationFlowTask, windowFlowTask).WaitAsync(HandoverBound);

            // Assert.
            //the exits balanced the enters
            Assert.Equal(0, inFlightOperations.ReadsCount);
            Assert.Equal(0, inFlightOperations.WritesCount);
        }

        [Fact]
        public async Task ExclusiveAccessWithoutReadLockDoesNotDrainInFlightReads()
        {
            /* With the reads left open they keep flowing during the whole window, so their
             * count has no zero to wait for: the window drains the writes only, and the
             * admitted read keeps running beside the exclusive work, like any read
             * starting inside the window would. */

            // Setup.
            //a wrongly awaited read would otherwise fail the window only at the default timeout
            options.ExclusiveAccessDrainTimeout = TimeSpan.FromSeconds(2);
            var operationGate = new TaskCompletionSource();
            collectionMock.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<FakeModel>>(), It.IsAny<FindOptions<FakeModel, FakeModel>>(), It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    await operationGate.Task;
                    return NewEmptyCursor();
                });
            var collection = engine.GetMongoCollection<FakeModel>("fakeModels");
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            // Action.
            var readTask = collection.FindAsync(Builders<FakeModel>.Filter.Empty);
            await engine.RunWithExclusiveAccessAsync(() =>
            {
                //the window opened with the read still in flight
                Assert.False(readTask.IsCompleted);
                return Task.CompletedTask;
            }, lockOnRead: false);

            // Assert.
            operationGate.SetResult();
            await readTask.WaitAsync(HandoverBound);
        }

        [Fact]
        public async Task ExclusiveAccessDrainFailsWhenAnOperationOutlivesTheTimeout()
        {
            /* Running the exclusive work beside an operation still in flight would break
             * the exclusivity the window declares: an operation outliving the drain
             * timeout denies the window, and the engine returns to normal access. */

            // Setup.
            options.ExclusiveAccessDrainTimeout = TimeSpan.FromMilliseconds(200);
            var operationGate = new TaskCompletionSource();
            collectionMock.Setup(c => c.InsertOneAsync(It.IsAny<FakeModel>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
                .Returns(operationGate.Task);
            var collection = engine.GetMongoCollection<FakeModel>("fakeModels");
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var exclusiveWorkRan = false;

            // Action.
            var operationTask = collection.InsertOneAsync(new FakeModel());
            var timeoutException = await Assert.ThrowsAsync<TimeoutException>(() =>
                engine.RunWithExclusiveAccessAsync(() =>
                {
                    exclusiveWorkRan = true;
                    return Task.CompletedTask;
                }).WaitAsync(HandoverBound));

            // Assert.
            //the exclusive work never ran beside the in flight write
            Assert.False(exclusiveWorkRan);
            //the failure names the db context and the operations left in flight
            Assert.Contains(nameof(FakeDbContext), timeoutException.Message, StringComparison.Ordinal);
            Assert.Contains("1 writes", timeoutException.Message, StringComparison.Ordinal);
            //the engine returns to normal access
            Assert.False(engine.IsExclusiveReadEnabled);
            Assert.False(engine.IsExclusiveWriteEnabled);

            //with the operation completed, the next window opens normally
            operationGate.SetResult();
            await operationTask.WaitAsync(HandoverBound);
            await engine.RunWithExclusiveAccessAsync(() =>
            {
                exclusiveWorkRan = true;
                return Task.CompletedTask;
            });
            Assert.True(exclusiveWorkRan);
        }

        [Fact]
        public async Task ExclusiveAccessDoesNotDrainAllowanceAdmittedOperations()
        {
            /* The flows admitted by an exclusive access allowance are the ones meant to
             * work during the window, like the dashboard status reads during a migration:
             * an operation they leave in flight doesn't hold the next window back. */

            // Setup.
            //a wrongly counted operation would otherwise fail the second window only at the default timeout
            options.ExclusiveAccessDrainTimeout = TimeSpan.FromSeconds(2);
            var operationGate = new TaskCompletionSource();
            collectionMock.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<FakeModel>>(), It.IsAny<FindOptions<FakeModel, FakeModel>>(), It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    await operationGate.Task;
                    return NewEmptyCursor();
                });
            var collection = engine.GetMongoCollection<FakeModel>("fakeModels");
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            // Action and assert.
            Task<IAsyncCursor<FakeModel>>? allowanceReadTask = null;
            await engine.RunWithExclusiveAccessAsync(() =>
            {
                //admitted by the allowance of this flow, and left in flight past the window
                allowanceReadTask = collection.FindAsync(Builders<FakeModel>.Filter.Empty);
                return Task.CompletedTask;
            });
            await engine.RunWithExclusiveAccessAsync(() =>
            {
                //the next window doesn't wait for it
                Assert.False(allowanceReadTask!.IsCompleted);
                return Task.CompletedTask;
            });

            operationGate.SetResult();
            await allowanceReadTask!.WaitAsync(HandoverBound);
        }

        [Fact]
        public async Task ExclusiveAccessDrainSeesNoResidueOfFailedAndDeniedOperations()
        {
            /* A failed operation exits its count with its fault, and a denied one with its
             * denial: neither leaves a residue the next drain would wait for. */

            // Setup.
            //a residue would otherwise fail the window only at the default timeout
            options.ExclusiveAccessDrainTimeout = TimeSpan.FromSeconds(2);
            collectionMock.Setup(c => c.InsertOneAsync(It.IsAny<FakeModel>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("fake driver failure"));
            var collection = engine.GetMongoCollection<FakeModel>("fakeModels");
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            // Action.
            //the driver failure surfaces from the operation
            await Assert.ThrowsAsync<InvalidOperationException>(() => collection.InsertOneAsync(new FakeModel()));

            //a foreign flow gets denied during the window
            async Task DeniedFlow()
            {
                using var deniedFlowContext = AsyncLocalContext.Instance.InitAsyncLocalContext();
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => collection.InsertOneAsync(new FakeModel()));
            }
            await engine.RunWithExclusiveAccessAsync(() => DeniedFlow());

            //no residue: the next window opens without waiting
            var windowRan = false;
            await engine.RunWithExclusiveAccessAsync(() =>
            {
                windowRan = true;
                return Task.CompletedTask;
            });

            // Assert.
            Assert.True(windowRan);
            var inFlightOperations = ((IInternalDbContextEngine)engine).InFlightOperations;
            Assert.Equal(0, inFlightOperations.ReadsCount);
            Assert.Equal(0, inFlightOperations.WritesCount);
        }

        [Fact]
        public async Task ExclusiveAccessAllowanceDoesNotOpenTheDatabaseOfOtherEngines()
        {
            /* The database a guarded collection hands out enforces the same scoping: with the
             * reads left open, it is reachable, and its writes stay closed to the allowance of
             * another engine. */

            // Setup.
            var otherDbContext = BuildDbContext(new DbContextOptions(), out var otherEngine);
            var lockedEngineAcquired = new TaskCompletionSource();
            var otherEngineVerified = new TaskCompletionSource();

            async Task LockingFlow()
            {
                using var flowContext = AsyncLocalContext.Instance.InitAsyncLocalContext();

                //lock the writes only, so the database stays readable
                await dbContext.Engine.RunWithExclusiveAccessAsync(async () =>
                {
                    lockedEngineAcquired.SetResult();
                    await otherEngineVerified.Task;
                }, lockOnRead: false);
            }
            async Task OtherEngineFlow()
            {
                using var flowContext = AsyncLocalContext.Instance.InitAsyncLocalContext();

                await lockedEngineAcquired.Task;
                try
                {
                    await otherDbContext.Engine.RunWithExclusiveAccessAsync(() =>
                        dbContext.FakeModels.AccessToCollectionAsync(async collection =>
                        {
                            //the allowance of the other engine doesn't open this database
                            var database = collection.Database;
                            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                                () => database.DropCollectionAsync("fakeModels"));
                            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                                () => database.CreateCollectionAsync("denied"));
                        }));
                }
                finally { otherEngineVerified.SetResult(); }
            }

            // Action and assert.
            await Task.WhenAll(LockingFlow(), OtherEngineFlow());

            (otherEngine as IDisposable)?.Dispose();
        }

        [Fact]
        public async Task ExclusiveAccessAllowanceDoesNotOpenOtherEngines()
        {
            // Setup.
            /* Every db context of a flow shares its execution context items, while exclusive
             * access is a per engine lock: an allowance granted by one engine must not open
             * another engine locked by someone else. */
            var otherDbContext = BuildDbContext(new DbContextOptions(), out var otherEngine);
            var lockedEngineAcquired = new TaskCompletionSource();
            var otherEngineVerified = new TaskCompletionSource();

            async Task LockingFlow()
            {
                using var flowContext = AsyncLocalContext.Instance.InitAsyncLocalContext();

                await dbContext.Engine.RunWithExclusiveAccessAsync(async () =>
                {
                    lockedEngineAcquired.SetResult();
                    await otherEngineVerified.Task;
                });
            }
            async Task OtherEngineFlow()
            {
                using var flowContext = AsyncLocalContext.Instance.InitAsyncLocalContext();

                await lockedEngineAcquired.Task;
                try
                {
                    await otherDbContext.Engine.RunWithExclusiveAccessAsync(async () =>
                    {
                        //the allowance belongs to the other engine: the locked one stays closed
                        await Assert.ThrowsAsync<UnauthorizedAccessException>(
                            () => dbContext.FakeModels.CreateAsync(new FakeModel { Id = "lockedId" }));
                        await Assert.ThrowsAsync<UnauthorizedAccessException>(
                            () => dbContext.FakeModels.FindOneAsync("lockedId"));

                        //the engine that granted the allowance keeps working
                        await otherDbContext.FakeModels.CreateAsync(new FakeModel { Id = "otherId" });
                    });
                }
                finally { otherEngineVerified.SetResult(); }
            }

            // Action and assert.
            await Task.WhenAll(LockingFlow(), OtherEngineFlow());

            (otherEngine as IDisposable)?.Dispose();
        }

        [Fact]
        public async Task NestedExclusiveAccessAllowancesCoexist()
        {
            // Setup.
            var otherDbContext = BuildDbContext(new DbContextOptions(), out var otherEngine);
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            // Action and assert.
            await dbContext.Engine.RunWithExclusiveAccessAsync(async () =>
            {
                await otherDbContext.Engine.RunWithExclusiveAccessAsync(async () =>
                {
                    //both engines are locked, and the flow holds an allowance of each one
                    await dbContext.FakeModels.CreateAsync(new FakeModel { Id = "id" });
                    await otherDbContext.FakeModels.CreateAsync(new FakeModel { Id = "otherId" });
                });

                //with the inner allowance disposed, the outer one still opens its own engine
                await dbContext.FakeModels.FindOneAsync("id");
            });

            (otherEngine as IDisposable)?.Dispose();
        }

        [Fact]
        public async Task SeedIfNeededSkipsOnReadOnlyDbContext()
        {
            // Setup.
            var readOnlyDbContext = BuildDbContext(
                new DbContextOptions { IsReadOnly = true },
                out var readOnlyEngine);

            // Action.
            var seeded = await readOnlyDbContext.SeedIfNeededAsync();

            // Assert.
            //no seeding, and no seeding state read from db either
            Assert.False(seeded);
            mongoDatabaseMock.Verify(d => d.GetCollection<OperationBase>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>()), Times.Never);

            (readOnlyEngine as IDisposable)?.Dispose();
        }

        [Fact]
        public void DbContextLockDeniesOnReadOnlyDbContext()
        {
            /* The lock collection is written raw, out of the read-only enforcement of the
             * guarded collections: claiming it would write on a database owned by another
             * application, that this db context can only read. */

            // Setup.
            BuildDbContext(new DbContextOptions { IsReadOnly = true }, out var readOnlyEngine);

            // Action and assert.
            var lockException = Assert.Throws<InvalidOperationException>(() => readOnlyEngine.DbContextLock);
            Assert.Contains("read-only", lockException.Message, StringComparison.Ordinal);

            (readOnlyEngine as IDisposable)?.Dispose();
        }

        [Fact]
        public void GetResourceLockDeniesOnReadOnlyDbContext()
        {
            /* Application locks write the same raw lock collection of the db context lock:
             * claiming one from a read-only db context would write on a database owned by
             * another application. */

            // Setup.
            BuildDbContext(new DbContextOptions { IsReadOnly = true }, out var readOnlyEngine);

            // Action and assert.
            var lockException = Assert.Throws<InvalidOperationException>(() =>
                readOnlyEngine.GetResourceLock("pins", "resource"));
            Assert.Contains("read-only", lockException.Message, StringComparison.Ordinal);

            (readOnlyEngine as IDisposable)?.Dispose();
        }

        [Fact]
        public void GetResourceLockValidatesNamespaceAndResourceId()
        {
            // Setup.
            BuildDbContext(new DbContextOptions(), out var engine);

            // Action and assert.
            //an empty namespace or resource id would collapse different locks onto one document
            Assert.Throws<ArgumentException>(() => engine.GetResourceLock("", "resource"));
            Assert.Throws<ArgumentException>(() => engine.GetResourceLock("pins", ""));
            /* A namespace carrying the separator would make different resources alias one
             * lock document: namespace "a" with id "b/c", and namespace "a/b" with id "c". */
            var separatorException = Assert.Throws<ArgumentException>(() =>
                engine.GetResourceLock("pins/nested", "resource"));
            Assert.Contains("/", separatorException.Message, StringComparison.Ordinal);

            (engine as IDisposable)?.Dispose();
        }

        [Fact]
        public async Task ResourceLockFacadesForwardToTheEngineLock()
        {
            /* The db context is the client surface of the resource locks: one call acquires
             * or inspects, without passing through the engine. */

            // Setup.
            var chosenLeaseDuration = TimeSpan.FromMinutes(3);
            var exclusiveLease = new Mock<IResourceLockLease>().Object;
            var sharedLease = new Mock<IResourceLockLease>().Object;
            var resourceLockMock = new Mock<IResourceLock>();
            resourceLockMock.Setup(l => l.TryAcquireAsync(ResourceLockMode.Exclusive, chosenLeaseDuration))
                .ReturnsAsync(exclusiveLease);
            resourceLockMock.Setup(l => l.TryAcquireAsync(ResourceLockMode.Shared, chosenLeaseDuration))
                .ReturnsAsync(sharedLease);
            resourceLockMock.Setup(l => l.IsLockedAsync())
                .ReturnsAsync(true);

            var mockedEngineMock = new Mock<IDbContextEngine>();
            mockedEngineMock.Setup(e => e.Options).Returns(new DbContextOptions());
            mockedEngineMock.Setup(e => e.GetResourceLock("pins", "resource"))
                .Returns(resourceLockMock.Object);

            var facadeDbContext = new FakeDbContext();
            facadeDbContext.AttachToEngine(mockedEngineMock.Object, [], new RepositoryRegistry());

            // Action and assert.
            //each facade addresses the lock of the given namespace and resource, verbatim
            Assert.Same(exclusiveLease,
                await facadeDbContext.TryAcquireResourceLockAsync("pins", "resource", leaseDuration: chosenLeaseDuration));
            Assert.Same(sharedLease,
                await facadeDbContext.TryAcquireResourceLockAsync("pins", "resource", ResourceLockMode.Shared, chosenLeaseDuration));
            Assert.True(await facadeDbContext.IsResourceLockedAsync("pins", "resource"));
        }

        [Fact]
        public async Task SeedIfNeededFailsWhenTheLockStaysHeldForTheWaitTimeout()
        {
            /* The caller blocks on the seeding, and the startup one waits on every db context
             * of the application: an owner never releasing the lock must fail this seeding,
             * instead of hanging the whole startup forever. */

            // Setup.
            var dbContextLockMock = new Mock<IResourceLock>();
            dbContextLockMock.Setup(l => l.TryClaimAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
                .ReturnsAsync(false);
            var seedingDbContext = BuildDbContextOnMockedEngine(
                dbContextLockMock.Object,
                new DbContextOptions());

            // Action.
            var seedingException = await Assert.ThrowsAsync<ScriniumDbSeedingException>(
                () => seedingDbContext.SeedIfNeededAsync(TimeSpan.FromMilliseconds(200)).WaitAsync(SeedingWaitBound));

            // Assert.
            //the failure names the db context and the requested timeout
            Assert.Contains(nameof(FakeDbContext), seedingException.Message, StringComparison.Ordinal);
            Assert.Contains("00:00:00.2", seedingException.Message, StringComparison.Ordinal);
            dbContextLockMock.Verify(l => l.TryClaimAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()), Times.AtLeast(2));
        }

        [Fact]
        public async Task SeedIfNeededClaimsTheLockWithItsLeaseDuration()
        {
            /* The seeding claim is the owner of the lock for the whole flow: its lease is how
             * long the db context stays locked if this instance dies while seeding. */

            // Setup.
            var chosenLeaseDuration = TimeSpan.FromMinutes(3);
            var dbContextLockMock = new Mock<IResourceLock>();
            dbContextLockMock.Setup(l => l.TryClaimAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
                .ReturnsAsync(true);
            dbContextLockMock.Setup(l => l.TryResumeClaimAsync(It.IsAny<string>()))
                .ReturnsAsync((IResourceLockLease?)null);
            var seedingDbContext = BuildDbContextOnMockedEngine(dbContextLockMock.Object, new DbContextOptions());

            // Action.
            //the resume denial stops the flow right after the claim, the only step under test here
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                seedingDbContext.SeedIfNeededAsync(lockLeaseDuration: chosenLeaseDuration));

            // Assert.
            dbContextLockMock.Verify(
                l => l.TryClaimAsync(It.IsAny<string>(), chosenLeaseDuration),
                Times.Once());
        }

        [Fact]
        public async Task SeedIfNeededClaimsTheLockWithTheDefaultLeaseDurationWithoutAnExplicitOne()
        {
            // Setup.
            var dbContextLockMock = new Mock<IResourceLock>();
            dbContextLockMock.Setup(l => l.TryClaimAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
                .ReturnsAsync(true);
            dbContextLockMock.Setup(l => l.TryResumeClaimAsync(It.IsAny<string>()))
                .ReturnsAsync((IResourceLockLease?)null);
            var seedingDbContext = BuildDbContextOnMockedEngine(dbContextLockMock.Object, new DbContextOptions());

            // Action.
            await Assert.ThrowsAsync<InvalidOperationException>(() => seedingDbContext.SeedIfNeededAsync());

            // Assert.
            dbContextLockMock.Verify(
                l => l.TryClaimAsync(It.IsAny<string>(), ResourceLock.DefaultLeaseDuration),
                Times.Once());
        }

        [Fact]
        public async Task SeedIfNeededReleasesTheClaimWhenItCantResume()
        {
            /* A claim resumed by nobody would deny every seeding and migration of the db
             * context, on every application instance, until its lease expiration. */

            // Setup.
            var dbContextLockMock = new Mock<IResourceLock>();
            dbContextLockMock.Setup(l => l.TryClaimAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
                .ReturnsAsync(true);
            dbContextLockMock.Setup(l => l.TryResumeClaimAsync(It.IsAny<string>()))
                .ReturnsAsync((IResourceLockLease?)null);
            var seedingDbContext = BuildDbContextOnMockedEngine(dbContextLockMock.Object, new DbContextOptions());

            // Action and assert.
            await Assert.ThrowsAsync<InvalidOperationException>(() => seedingDbContext.SeedIfNeededAsync());
            dbContextLockMock.Verify(l => l.TryReleaseAsync(It.IsAny<string>()), Times.Once());
        }

        [Fact]
        public async Task SeedIfNeededWaitsForTheLockLeaseDurationByDefault()
        {
            /* A dead owner stops renewing its lease, that expires inside the default wait:
             * only an owner still alive, and working longer than it, fails the seeding. */

            // Setup.
            var dbContextLockMock = new Mock<IResourceLock>();
            dbContextLockMock.Setup(l => l.TryClaimAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
                .ReturnsAsync(false);
            var seedingDbContext = BuildDbContextOnMockedEngine(dbContextLockMock.Object, new DbContextOptions());

            // Action.
            //without an explicit wait, the lease duration of this seeding is the one bounding it
            var seedingException = await Assert.ThrowsAsync<ScriniumDbSeedingException>(
                () => seedingDbContext.SeedIfNeededAsync(lockLeaseDuration: TimeSpan.FromMilliseconds(200))
                                      .WaitAsync(SeedingWaitBound));

            // Assert.
            Assert.Contains("00:00:00.2", seedingException.Message, StringComparison.Ordinal);
            dbContextLockMock.Verify(l => l.TryClaimAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()), Times.AtLeast(2));
        }

        // Helpers.
        private FakeDbContext BuildDbContext(DbContextOptions dbContextOptions, out IDbContextEngine dbContextEngine)
        {
            var newDbContext = new FakeDbContext();
            dbContextEngine = newDbContext.BuildEngine(
                dependenciesMock.Object,
                mongoClientMock.Object,
                dbContextOptions);
            newDbContext.AttachToEngine(dbContextEngine, [], dependenciesMock.Object.RepositoryRegistry);
            return newDbContext;
        }

        /* The seeding lock flow lives entirely on the engine: a mocked engine drives its lock
         * and its seeding cache, without any database behind. */
        private static FakeDbContext BuildDbContextOnMockedEngine(
            IResourceLock dbContextLock,
            DbContextOptions dbContextOptions)
        {
            bool? isSeededCache = false;
            var mockedEngineMock = new Mock<IDbContextEngine>();
            mockedEngineMock.Setup(e => e.DbContextLock).Returns(dbContextLock);
            mockedEngineMock.Setup(e => e.Options).Returns(dbContextOptions);
            mockedEngineMock.SetupGet(e => e.IsSeededCache).Returns(() => isSeededCache);
            //the db re-reads of the wait loop keep reporting the database as not seeded
            mockedEngineMock.SetupSet<bool?>(e => e.IsSeededCache = It.IsAny<bool?>())
                .Callback(value => isSeededCache = value ?? false);

            var newDbContext = new FakeDbContext();
            newDbContext.AttachToEngine(mockedEngineMock.Object, [], new RepositoryRegistry());
            return newDbContext;
        }

        private static ClusterDescription NewClusterDescription(ClusterType type) =>
            new(new ClusterId(0), false, null, type, []);

        private static void MarkModelChanged(IInternalDbContext targetDbContext, IEntityModel model)
        {
            //track the model with a model document, then flag it changed, like a mutation would
            targetDbContext.SetModelBsonDocument(model, []);
            targetDbContext.MarkChangeCandidate(model);
        }

        /* Proxies are bound to their source repository right after creation: bind the hand
         * constructed ones like the proxy generator does, or they carry no origin. */
        private FakeModelProxy NewBoundProxy(string id)
        {
            var proxy = new FakeModelProxy { Id = id };
            ((IProxyModel)proxy).BindProxy(dbContext, dbContext.FakeModels);
            return proxy;
        }

        private static Mock<IEntityModel> NewChangedModelMock(IRepository sourceRepository)
        {
            var modelMock = new Mock<IEntityModel>();
            modelMock.As<IReferenceable>().Setup(r => r.SourceRepository).Returns(sourceRepository);
            return modelMock;
        }

        private static IAsyncCursor<FakeModel> NewEmptyCursor()
        {
            var cursorMock = new Mock<IAsyncCursor<FakeModel>>();
            cursorMock.Setup(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            return cursorMock.Object;
        }

        private static Mock<IRepository> NewSourceRepositoryMock()
        {
            var repositoryMock = new Mock<IRepository>();
            repositoryMock.Setup(r => r.ModelIdToString(It.IsAny<object>()))
                .Returns("id");
            return repositoryMock;
        }

        private string[] RenderedInIds(FilterDefinition<FakeModel> filter)
        {
            /* Render against a frozen class map serializer of the model, resolving the id
             * member to its element name like the collection query pipeline does. */
            var classMap = new BsonClassMap<FakeModel>();
            classMap.AutoMap();
            classMap.Freeze();
            var renderedFilter = filter.Render(new(
                new BsonClassMapSerializer<FakeModel>(classMap),
                engine.SerializerRegistry));
            return renderedFilter["_id"]["$in"].AsBsonArray
                .Select(id => id.AsString)
                .ToArray();
        }

        private void SetReplicaSetTopology() =>
            clusterMock.Setup(c => c.Description)
                .Returns(NewClusterDescription(ClusterType.ReplicaSet));
    }
}