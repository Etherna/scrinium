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

using Etherna.MongODM.Core.ExecContext.AsyncLocal;
using Etherna.MongODM.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Etherna.MongODM.Core.Utility
{
    public class LoadedModelsTrackerTest
    {
        // Fields.
        private readonly Mock<IDbContextEngine> dbContextEngineMock = new();
        private readonly LoadedModelsTracker loadedModelsTracker = new();

        // Constructor.
        public LoadedModelsTrackerTest()
        {
            dbContextEngineMock.Setup(c => c.ExecutionContext)
                .Returns(AsyncLocalContext.Instance);
            dbContextEngineMock.Setup(c => c.Identifier)
                .Returns("FakeDbContext");
            dbContextEngineMock.Setup(c => c.Options.DbName)
                .Returns("fakeDb");

            loadedModelsTracker.Initialize(dbContextEngineMock.Object, Mock.Of<ILogger>());
        }

        // Tests.
        [Fact]
        public void ClearTrackedRemovesAllModels()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            loadedModelsTracker.TrackModel(new FakeModel { Id = "id0" });
            loadedModelsTracker.TrackModel(new FakeModel { Id = "id1" });

            // Action.
            loadedModelsTracker.ClearTracked();

            // Assert.
            Assert.Empty(loadedModelsTracker.LoadedModels);
        }

        [Fact]
        public void TrackedModelsAreIsolatedBetweenScopes()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var model = new FakeModel { Id = "id0" };
            loadedModelsTracker.TrackModel(model);

            // Action and assert.
            using (var nestedContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext())
            {
                //nested scope starts empty
                Assert.Empty(loadedModelsTracker.LoadedModels);

                loadedModelsTracker.TrackModel(new FakeModel { Id = "id1" });
            }

            //original scope is restored, untouched by nested one
            var loadedModel = Assert.Single(loadedModelsTracker.LoadedModels);
            Assert.Same(model, loadedModel);
        }

        [Fact]
        public void TrackModelAddsModelToCurrentScope()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var model = new FakeModel { Id = "id0" };

            // Action.
            loadedModelsTracker.TrackModel(model);

            // Assert.
            var loadedModel = Assert.Single(loadedModelsTracker.LoadedModels);
            Assert.Same(model, loadedModel);
        }

        [Fact]
        public void UntrackModelRemovesModelInstance()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var model0 = new FakeModel { Id = "id0" };
            var model1 = new FakeModel { Id = "id1" };
            loadedModelsTracker.TrackModel(model0);
            loadedModelsTracker.TrackModel(model1);

            // Action.
            loadedModelsTracker.UntrackModel(model0);

            // Assert.
            var loadedModel = Assert.Single(loadedModelsTracker.LoadedModels);
            Assert.Same(model1, loadedModel);
        }
    }
}
