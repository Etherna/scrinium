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
using Etherna.MongODM.Core.ExecContext.AsyncLocal;
using Etherna.MongODM.Core.ProxyModels;
using Etherna.MongODM.IntegrationTests.Fixtures;
using Etherna.MongODM.IntegrationTests.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.MongODM.IntegrationTests
{
    [Collection("Integration")]
    public class ReferencedModelsTests : IDisposable
    {
        // Fields.
        private readonly ITestDbContext dbContext;
        private readonly IntegrationFixture fixture;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public ReferencedModelsTests(IntegrationFixture fixture)
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
        public async Task FullLoadUpgradesSummaryReferenceInPlace()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var (blog, post) = await CreateBlogWithPostAsync();

            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedBlog = await dbContext.Blogs.FindOneAsync(blog.Id);
            var referencedPost = loadedBlog.LastPost!;
            Assert.True(((IReferenceable)referencedPost).IsSummary);

            // Action.
            var foundPost = await dbContext.Posts.FindOneAsync(post.Id);

            // Assert.
            //the full load returns the already loaded summary instance, upgraded in place
            Assert.Same(referencedPost, foundPost);
            Assert.False(((IReferenceable)referencedPost).IsSummary);
            Assert.Equal("post content", referencedPost.Content);

            //the merge doesn't pollute change auditing
            Assert.Empty(dbContext.ChangedModelsList);
        }

        [Fact]
        public async Task LaterSummaryDoesntOverwriteLoadedMembers()
        {
            /* Two summaries of the same document are denormalized copies from different origin
             * documents, updated at different times: neither is authoritative. A member already
             * loaded on the canonical instance is never overwritten by a later summary, also
             * keeping values stable for who already read them on the scope. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("post title", "post content");
            await dbContext.Posts.CreateAsync(post);

            var blog1 = new Blog("blog 1");
            blog1.AddPost(post);
            await dbContext.Blogs.CreateAsync(blog1);

            var blog2 = new Blog("blog 2");
            blog2.AddPost(post);
            await dbContext.Blogs.CreateAsync(blog2);

            //make blog1's denormalized copy stale, like a not yet executed dependencies update
            var blogsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("blogs");
            await blogsCollection.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(blog1.Id)),
                Builders<BsonDocument>.Update.Set("LastPost.Title", "stale title"));

            // Action.
            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedBlog2 = await dbContext.Blogs.FindOneAsync(blog2.Id);
            var canonicalPost = loadedBlog2.LastPost!;
            Assert.Equal("post title", canonicalPost.Title);

            var loadedBlog1 = await dbContext.Blogs.FindOneAsync(blog1.Id);

            // Assert.
            //the stale summary deduplicates to the canonical, without overwriting its members
            Assert.Same(canonicalPost, loadedBlog1.LastPost);
            Assert.Equal("post title", canonicalPost.Title);
        }

        [Fact]
        public async Task LaterSummaryMergesIntoLoadedReference()
        {
            /* An id only reference loaded first becomes the canonical instance. A later
             * summary of the same document, carrying denormalized members, must merge them
             * into the canonical instead of being discarded. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var postA = new Post("title A", "content A");
            var postB = new Post("title B", "content B");
            await dbContext.Posts.CreateAsync(postA);
            await dbContext.Posts.CreateAsync(postB);

            //blog1: LastPost preview is postB, Posts collection references postA by id only
            var blog1 = new Blog("blog 1");
            blog1.AddPost(postA);
            blog1.AddPost(postB);
            await dbContext.Blogs.CreateAsync(blog1);

            //blog2: LastPost preview is postA, with its denormalized Title
            var blog2 = new Blog("blog 2");
            blog2.AddPost(postA);
            await dbContext.Blogs.CreateAsync(blog2);

            // Action.
            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedBlog1 = await dbContext.Blogs.FindOneAsync(blog1.Id);
            var canonicalPostA = loadedBlog1.Posts.Single(p => p.Id == postA.Id);
            Assert.DoesNotContain(nameof(Post.Title), ((IReferenceable)canonicalPostA).SettedMemberNames);

            var loadedBlog2 = await dbContext.Blogs.FindOneAsync(blog2.Id);

            // Assert.
            //the preview deserialization returned the canonical instance, merged with Title
            Assert.Same(canonicalPostA, loadedBlog2.LastPost);
            Assert.True(((IReferenceable)canonicalPostA).IsSummary);
            Assert.Contains(nameof(Post.Title), ((IReferenceable)canonicalPostA).SettedMemberNames);
            Assert.Equal("title A", canonicalPostA.Title);

            //the merge doesn't pollute change auditing
            Assert.Empty(dbContext.ChangedModelsList);
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

            //simulate a concurrent update from a different DI scope
            using (var externalScope = fixture.ServiceProvider.CreateScope())
            {
                var externalDbContext = externalScope.ServiceProvider.GetRequiredService<ITestDbContext>();
                var externalPost = await externalDbContext.Posts.FindOneAsync(post.Id);
                externalPost.Content = "updated externally";
                await externalDbContext.SaveChangesAsync();
            }

            // Action.
            //lazy load happens now, after the external update
            var content = referencedPost.Content;

            // Assert.
            Assert.Equal("updated externally", content);
        }

        [Fact]
        public async Task PreviewAndCollectionReferencesShareTheSameInstance()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var (blog, post) = await CreateBlogWithPostAsync();

            // Action.
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedBlog = await dbContext.Blogs.FindOneAsync(blog.Id);

            // Assert.
            //identity map: preview member and collection element are the same instance
            Assert.NotNull(loadedBlog.LastPost);
            Assert.Equal(post.Id, loadedBlog.LastPost!.Id);
            Assert.Same(loadedBlog.LastPost, loadedBlog.Posts.Single());

            //the canonical instance exposes its partially loaded data
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

        [Fact]
        public async Task ReferenceToAlreadyLoadedModelReturnsSameInstance()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var (blog, post) = await CreateBlogWithPostAsync();

            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var foundPost = await dbContext.Posts.FindOneAsync(post.Id);

            // Action.
            var loadedBlog = await dbContext.Blogs.FindOneAsync(blog.Id);

            // Assert.
            //references deserialization returns the full instance already loaded
            Assert.Same(foundPost, loadedBlog.LastPost);
            Assert.Same(foundPost, loadedBlog.Posts.Single());
            Assert.Equal("post content", loadedBlog.LastPost!.Content);
        }

        [Fact]
        public async Task ReferenceWithUnrecognizedSchemaIdLoadsIdOnlyAndLazyLoads()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var (blog, post) = await CreateBlogWithPostAsync();

            //shape the reference as written by an unknown legacy schema, with incompatible members
            var blogsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("blogs");
            await blogsCollection.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(blog.Id)),
                Builders<BsonDocument>.Update
                    .Set("LastPost._m", "unknown-schema-id")
                    .Set("LastPost.Title", 42)
                    .Set("LastPost.Content", "legacy content"));

            // Action.
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedBlog = await dbContext.Blogs.FindOneAsync(blog.Id);
            var referencedPost = loadedBlog.LastPost!;

            // Assert.
            //only the id is deserialized from the unrecognized reference document
            Assert.Equal(post.Id, referencedPost.Id);
            Assert.True(((IReferenceable)referencedPost).IsSummary);
            Assert.DoesNotContain(nameof(Post.Title), ((IReferenceable)referencedPost).SettedMemberNames);

            //any other member lazy loads from the origin document
            Assert.Equal("post title", referencedPost.Title);
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
