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
using System.Threading.Tasks;
using Xunit;

namespace Etherna.MongODM.IntegrationTests
{
    [Collection("Integration")]
    public class CrudAndTrackingTests(IntegrationFixture fixture)
    {
        // Fields.
        private readonly ITestDbContext dbContext = fixture.TestDbContext;

        // Tests.
        [Fact]
        public async Task ChangedModelsListContainsOnlyMutatedModels()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post0 = new Post("title0", "content0");
            var post1 = new Post("title1", "content1");
            await dbContext.Posts.CreateAsync(post0);
            await dbContext.Posts.CreateAsync(post1);

            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedPost0 = await dbContext.Posts.FindOneAsync(post0.Id);
            var loadedPost1 = await dbContext.Posts.FindOneAsync(post1.Id);

            // Action.
            loadedPost0.Content = "updated content0";

            // Assert.
            var changedModel = Assert.Single(dbContext.ChangedModelsList);
            Assert.Same(loadedPost0, changedModel);
        }

        [Fact]
        public async Task CreateAndFindModel()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("title", "content");
            await dbContext.Posts.CreateAsync(post);

            // Action.
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var foundPost = await dbContext.Posts.FindOneAsync(post.Id);

            // Assert.
            Assert.Equal(post.Id, foundPost.Id);
            Assert.Equal("title", foundPost.Title);
            Assert.Equal("content", foundPost.Content);
        }

        [Fact]
        public async Task DeletedModelIsNotSavedAgain()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("title", "content");
            await dbContext.Posts.CreateAsync(post);

            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedPost = await dbContext.Posts.FindOneAsync(post.Id);
            loadedPost.Content = "updated content";

            // Action.
            await dbContext.Posts.DeleteAsync(loadedPost);
            await dbContext.SaveChangesAsync();

            // Assert.
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var foundPost = await dbContext.Posts.TryFindOneAsync(post.Id);
            Assert.Null(foundPost);
        }

        [Fact]
        public async Task DistinctInstancesForSameDocumentInSameScope()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("title", "content");
            await dbContext.Posts.CreateAsync(post);

            // Action.
            var firstLoadedPost = await dbContext.Posts.FindOneAsync(post.Id);
            var secondLoadedPost = await dbContext.Posts.FindOneAsync(post.Id);

            // Assert.
            //no identity map: same document, distinct instances
            Assert.NotSame(firstLoadedPost, secondLoadedPost);
            Assert.Equal(firstLoadedPost.Id, secondLoadedPost.Id);
        }

        [Fact]
        public async Task FindOneAlwaysReadsFreshDataAfterExternalUpdate()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("title", "content");
            await dbContext.Posts.CreateAsync(post);

            var firstLoadedPost = await dbContext.Posts.FindOneAsync(post.Id);

            //simulate a concurrent update from an isolated execution scope
            using (var externalContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext())
            {
                var externalPost = await dbContext.Posts.FindOneAsync(post.Id);
                externalPost.Content = "updated externally";
                await dbContext.SaveChangesAsync();
            }

            // Action.
            var secondLoadedPost = await dbContext.Posts.FindOneAsync(post.Id);

            // Assert.
            //no identity map: a new read always hits the database
            Assert.NotSame(firstLoadedPost, secondLoadedPost);
            Assert.Equal("content", firstLoadedPost.Content);
            Assert.Equal("updated externally", secondLoadedPost.Content);
        }

        [Fact]
        public async Task NoTrackingModifierSkipsTracking()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("title", "content");
            await dbContext.Posts.CreateAsync(post);

            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            Post loadedPost;
            using (dbContext.SerializerModifierAccessor.EnableCacheSerializerModifier(noCache: true))
            {
                loadedPost = await dbContext.Posts.FindOneAsync(post.Id);
            }

            // Action.
            loadedPost.Content = "updated content";
            await dbContext.SaveChangesAsync();

            // Assert.
            //the model was loaded without tracking, so its changes are not persisted
            Assert.Empty(dbContext.ChangedModelsList);

            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var foundPost = await dbContext.Posts.FindOneAsync(post.Id);
            Assert.Equal("content", foundPost.Content);
        }
    }
}
