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
using Etherna.MongODM.Core.ExecContext.AsyncLocal;
using Etherna.MongODM.Core.Models;
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

        private readonly Mock<IMongoCollection<FakeModel>> collectionMock = new();
        private readonly Mock<IDbDependencies> dependenciesMock = new();
        private readonly Mock<ILoadedModelsTracker> loadedModelsTrackerMock = new();
        private readonly Mock<IMongoClient> mongoClientMock = new();
        private readonly Mock<IMongoDatabase> mongoDatabaseMock = new();
        
        // Constructor.
        public DbContextTest()
        {
            loadedModelsTrackerMock.Setup(t => t.LoadedModels)
                .Returns([]);
            
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
            dependenciesMock.Setup(d => d.LoadedModelsTracker)
                .Returns(loadedModelsTrackerMock.Object);
            dependenciesMock.Setup(d => d.MapRegistry)
                .Returns(new Mock<IMapRegistry>().Object);
            dependenciesMock.Setup(d => d.ProxyGenerator)
                .Returns(new Mock<IProxyGenerator>().Object);
            dependenciesMock.Setup(d => d.RepositoryRegistry)
                .Returns(new RepositoryRegistry());

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
            
            dbContext = new FakeDbContext();
            engine = dbContext.BuildEngine(
                dependenciesMock.Object,
                mongoClientMock.Object,
                new DbContextOptions());
            dbContext.AttachToEngine(engine, [], dependenciesMock.Object.RepositoryRegistry);
        }

        // Dispose.
        public void Dispose()
        {
            (engine as IDisposable)?.Dispose();
            GC.SuppressFinalize(this);
        }
        
        // Tests.
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
    }
}