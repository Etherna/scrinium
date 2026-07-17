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
using Etherna.MongODM.IntegrationTests.Fixtures;
using Etherna.MongODM.IntegrationTests.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.MongODM.IntegrationTests
{
    [Collection("Integration")]
    public class DeleteManyTests : IDisposable
    {
        // Fields.
        private readonly ITestDbContext dbContext;
        private readonly IntegrationFixture fixture;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public DeleteManyTests(IntegrationFixture fixture)
        {
            this.fixture = fixture;
            serviceScope = fixture.ServiceProvider.CreateScope();
            dbContext = serviceScope.ServiceProvider.GetRequiredService<ITestDbContext>();
        }

        public void Dispose()
        {
            serviceScope.Dispose();
            GC.SuppressFinalize(this);
        }

        // Tests.
        [Fact]
        public async Task DeleteManyRemovesMatchingDocuments()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var postKeep = new Post("title to keep", "content");
            var postDelete0 = new Post("title to delete", "content 0");
            var postDelete1 = new Post("title to delete", "content 1");
            await dbContext.Posts.CreateAsync(postKeep);
            await dbContext.Posts.CreateAsync(postDelete0);
            await dbContext.Posts.CreateAsync(postDelete1);

            // Action.
            var deletedCount = await dbContext.Posts.DeleteManyAsync(p => p.Title == "title to delete");

            // Assert.
            Assert.Equal(2, deletedCount);

            using var readScope = fixture.ServiceProvider.CreateScope();
            var readDbContext = readScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            Assert.NotNull(await readDbContext.Posts.TryFindOneAsync(postKeep.Id));
            Assert.Null(await readDbContext.Posts.TryFindOneAsync(postDelete0.Id));
            Assert.Null(await readDbContext.Posts.TryFindOneAsync(postDelete1.Id));
        }

        [Fact]
        public async Task DeleteManyDoesntTouchTheScopeAndSavesStaySafe()
        {
            /* The bulk delete is a raw operation: instances already loaded that match the
             * filter stay on their scope. A following changes save must not recreate the
             * deleted document, nor enqueue a dependencies update task doomed to fail. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("title", "content");
            await dbContext.Posts.CreateAsync(post);

            var loadedPost = await dbContext.Posts.FindOneAsync(post.Id);
            loadedPost.Content = "updated content";
            Assert.Single(dbContext.ChangedModelsList);

            fixture.TaskRunner.ClearPending();

            // Action.
            var deletedCount = await dbContext.Posts.DeleteManyAsync(p => p.Id == post.Id);

            // Assert.
            Assert.Equal(1, deletedCount);

            //raw semantics: the loaded instance stays on the scope, and a find by id
            //keeps returning it through the identity map instead of failing as not found
            Assert.Same(loadedPost, dbContext.TryGetLoadedModel(typeof(Post), post.Id!));
            Assert.Same(loadedPost, await dbContext.Posts.FindOneAsync(post.Id));
            Assert.Single(dbContext.ChangedModelsList);

            // Action.
            await dbContext.SaveChangesAsync();

            // Assert.
            //the save consumed the pending change without recreating the document,
            //and without enqueueing a dependencies update task
            Assert.Empty(dbContext.ChangedModelsList);
            Assert.Equal(0, fixture.TaskRunner.PendingCount);

            using var readScope = fixture.ServiceProvider.CreateScope();
            var readDbContext = readScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            Assert.Null(await readDbContext.Posts.TryFindOneAsync(post.Id));
        }
    }
}
