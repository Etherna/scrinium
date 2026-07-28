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
using Etherna.MongoDB.Driver.Core.Clusters;
using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.ExecContext.AsyncLocal;
using Etherna.MongODM.Core.Models;
using Etherna.MongODM.Core.Exceptions;
using Etherna.MongODM.Core.Options;
using Etherna.MongODM.Core.ProxyModels;
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.Core.Serialization.Mapping;
using Etherna.MongODM.Core.Utility;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.MongODM.Core
{
    public class DbContextTest : IDisposable
    {
        // Fields.
        private readonly FakeDbContext dbContext;
        private readonly IDbContextEngine engine;

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
            options.ImplicitLazyLoad = ImplicitLazyLoadMode.Throw;

            // Action and assert.
            var exception = Assert.Throws<MongodmLazyLoadingException>(
                () => dbContext.OnImplicitLazyLoad(typeof(FakeModel), "StringProp"));
            Assert.Contains(nameof(FakeModel), exception.Message, StringComparison.Ordinal);
            Assert.Contains("StringProp", exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(ImplicitLazyLoadMode.Silent)]
        [InlineData(ImplicitLazyLoadMode.Warn)]
        public void OnImplicitLazyLoadAllowsLoadsWithNotThrowingModes(ImplicitLazyLoadMode mode)
        {
            // Setup.
            options.ImplicitLazyLoad = mode;

            // Action, asserting no throw.
            dbContext.OnImplicitLazyLoad(typeof(FakeModel), "StringProp");
            dbContext.OnImplicitLazyLoad(typeof(FakeModel), "StringProp"); //repeated: warn dedups per scope
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
            dbContext.RegisterLoadedModel("id", model);

            // Assert.
            Assert.Same(model, dbContext.TryGetLoadedModel(typeof(FakeModel), "id"));
            Assert.Null(dbContext.TryGetLoadedModel(typeof(FakeModel), "otherId"));

            // Action.
            dbContext.UnregisterLoadedModel("id", model);

            // Assert.
            Assert.Null(dbContext.TryGetLoadedModel(typeof(FakeModel), "id"));
        }

        [Fact]
        public void ReplaceOutdatedLoadedModelSwapsTheLoadedInstance()
        {
            // Setup.
            var outdatedModel = new FakeModelProxy { Id = "id" };
            var currentModel = new FakeModelProxy { Id = "id" };
            dbContext.RegisterLoadedModel("id", outdatedModel);

            // Action.
            dbContext.ReplaceOutdatedLoadedModel("id", outdatedModel, currentModel);

            // Assert.
            //the fresh instance becomes the loaded one, and only the outdated one is flagged
            Assert.Same(currentModel, dbContext.TryGetLoadedModel(typeof(FakeModel), "id"));
            Assert.True(dbContext.IsOutdatedModel(outdatedModel));
            Assert.False(dbContext.IsOutdatedModel(currentModel));
        }

        [Fact]
        public void ReplaceOutdatedLoadedModelValidatesTheModelIds()
        {
            // Setup.
            var outdatedModel = new FakeModelProxy { Id = "id" };
            var currentModel = new FakeModelProxy { Id = "otherId" };
            dbContext.RegisterLoadedModel("id", outdatedModel);

            // Action.
            var exception = Assert.Throws<ArgumentException>(
                () => dbContext.ReplaceOutdatedLoadedModel("id", outdatedModel, currentModel));

            // Assert.
            //the mismatch fails fast, before any state mutation
            Assert.Equal("currentModel", exception.ParamName);
            Assert.False(dbContext.IsOutdatedModel(outdatedModel));
            Assert.Same(outdatedModel, dbContext.TryGetLoadedModel(typeof(FakeModel), "id"));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CanRunExclusiveAccess(bool lockOnRead)
        {
            var fakeModel = new FakeModel { Id = "id" };

            async Task Process1()
            {
                using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
                
                //succeeds without exclusive access
                await dbContext.FakeModels.CreateAsync(fakeModel);
                await dbContext.FakeModels.FindOneAsync("test");

                await Task.Delay(500);
                
                //fails with exclusive access without an allowed area. Can read if not locked
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => dbContext.FakeModels.CreateAsync(fakeModel));
                if (lockOnRead)
                    await Assert.ThrowsAsync<UnauthorizedAccessException>(() => dbContext.FakeModels.FindOneAsync("test"));
                else
                    await dbContext.FakeModels.FindOneAsync("test");
                
                await Task.Delay(500);
                
                //succeeds without exclusive access
                await dbContext.FakeModels.CreateAsync(fakeModel);
                await dbContext.FakeModels.FindOneAsync("test");
            }
            async Task Process2()
            {
                using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
                
                //succeeds without exclusive access
                await dbContext.FakeModels.CreateAsync(fakeModel);
                await dbContext.FakeModels.FindOneAsync("test");
                
                await Task.Delay(250);
                
                //run exclusive access with allowed area
                var result = await dbContext.Engine.RunWithExclusiveAccessAsync(async () =>
                {
                    //succeed with exclusive access in allowed area
                    await dbContext.FakeModels.CreateAsync(fakeModel);
                    await dbContext.FakeModels.FindOneAsync("test");
                    
                    await Task.Delay(500);
                    
                    return 42;
                }, lockOnRead);
                Assert.Equal(42, result);
                
                //succeeds without exclusive access
                await dbContext.FakeModels.CreateAsync(fakeModel);
                await dbContext.FakeModels.FindOneAsync("test");
            }

            await Task.WhenAll(Process1(), Process2());
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

        private static ClusterDescription NewClusterDescription(ClusterType type) =>
            new(new ClusterId(0), false, null, type, []);

        private static void MarkModelChanged(IDbContext targetDbContext, IEntityModel model)
        {
            //track the model with a model document, then flag it changed, like a mutation would
            targetDbContext.SetModelBsonDocument(model, []);
            targetDbContext.MarkChangeCandidate(model);
        }

        private static Mock<IEntityModel> NewChangedModelMock(IRepository sourceRepository)
        {
            var modelMock = new Mock<IEntityModel>();
            modelMock.As<IReferenceable>().Setup(r => r.SourceRepository).Returns(sourceRepository);
            return modelMock;
        }

        private static Mock<IRepository> NewSourceRepositoryMock()
        {
            var repositoryMock = new Mock<IRepository>();
            repositoryMock.Setup(r => r.ModelIdToString(It.IsAny<object>()))
                .Returns("id");
            return repositoryMock;
        }

        private void SetReplicaSetTopology() =>
            clusterMock.Setup(c => c.Description)
                .Returns(NewClusterDescription(ClusterType.ReplicaSet));
    }
}