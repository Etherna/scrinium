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
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.MongODM.IntegrationTests
{
    [Collection("Integration")]
    public class ReferencedModelsTests(IntegrationFixture fixture)
    {
        // Fields.
        private readonly ITestDbContext dbContext = fixture.TestDbContext;

        // Tests.
        [Fact]
        public async Task ChangedReferencedModelIsPersistedBySaveChanges()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var (blog, post) = await CreateBlogWithPostAsync();

            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedBlog = await dbContext.Blogs.FindOneAsync(blog.Id);
            var referencedPost = loadedBlog.Posts.Single();

            // Action.
            referencedPost.Content = "updated content";
            await dbContext.SaveChangesAsync();

            // Assert.
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var foundPost = await dbContext.Posts.FindOneAsync(post.Id);
            Assert.Equal("updated content", foundPost.Content);
        }

        [Fact]
        public async Task LazyLoadReadsFreshDataAfterExternalUpdate()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var (blog, post) = await CreateBlogWithPostAsync();

            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedBlog = await dbContext.Blogs.FindOneAsync(blog.Id);
            var referencedPost = loadedBlog.Posts.Single();

            //simulate a concurrent update from an isolated execution scope
            using (var externalContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext())
            {
                var externalPost = await dbContext.Posts.FindOneAsync(post.Id);
                externalPost.Content = "updated externally";
                await dbContext.SaveChangesAsync();
            }

            // Action.
            //lazy load happens now, after the external update
            var content = referencedPost.Content;

            // Assert.
            Assert.Equal("updated externally", content);
        }

        [Fact]
        public async Task PreviewAndCollectionReferencesAreDistinctInstances()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var (blog, post) = await CreateBlogWithPostAsync();

            // Action.
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedBlog = await dbContext.Blogs.FindOneAsync(blog.Id);

            // Assert.
            //no identity map: preview member and collection element are distinct instances
            Assert.NotNull(loadedBlog.LastPost);
            Assert.Equal(post.Id, loadedBlog.LastPost!.Id);
            Assert.Equal(post.Id, loadedBlog.Posts.Single().Id);
            Assert.NotSame(loadedBlog.LastPost, loadedBlog.Posts.Single());

            //preview member exposes its partially loaded data
            Assert.Equal("post title", loadedBlog.LastPost.Title);
        }

        [Fact]
        public async Task ReferencedModelsLoadAsSummaryAndLazyLoadFullDocument()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var (blog, post) = await CreateBlogWithPostAsync();

            // Action.
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedBlog = await dbContext.Blogs.FindOneAsync(blog.Id);
            var referencedPost = loadedBlog.Posts.Single();

            // Assert.
            Assert.Equal(post.Id, referencedPost.Id);

            //accessing an unloaded member triggers the lazy full document load
            Assert.Equal("post content", referencedPost.Content);
        }

        // Helpers.
        private async Task<(Blog blog, Post post)> CreateBlogWithPostAsync()
        {
            var post = new Post("post title", "post content");
            await dbContext.Posts.CreateAsync(post);

            var blog = new Blog("blog title");
            blog.AddPost(post);
            await dbContext.Blogs.CreateAsync(blog);

            return (blog, post);
        }
    }
}
