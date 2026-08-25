// Copyright 2020-present Etherna SA
// This file is part of Scrinium.
//
// Scrinium is free software: you can redistribute it and/or modify it under the terms of the
// GNU Lesser General Public License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
//
// Scrinium is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY;
// without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License along with Scrinium.
// If not, see <https://www.gnu.org/licenses/>.

using Etherna.MongoDB.Bson;
using Etherna.MongoDB.Driver;
using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.Exceptions;
using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Etherna.Scrinium.Core.Options;
using Etherna.Scrinium.Core.ProxyModels;
using Etherna.Scrinium.IntegrationTests.Fixtures;
using Etherna.Scrinium.IntegrationTests.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.IntegrationTests
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
        public void EmbeddedEntityModelMemberFailsAtInitialization()
        {
            /* An entity model serialized as a full embedded document instead of being
             * referenced is a configuration error: it must fail fast at engine
             * initialization, detailing the involved members. */

            // Setup.
            var dbContext = new InvalidEmbeddedEntityDbContext();
            var dependencies = fixture.ServiceProvider.GetRequiredService<IDbDependencies>();
            var options = new DbContextOptions
            {
                ConnectionString = $"{fixture.MongoDbUrl}/scrinium-it-invalid-embedded"
            };

            // Action & assert.
            var exception = Assert.Throws<ScriniumEmbeddedEntityModelException>(
                () => dbContext.BuildEngine(dependencies, new MongoClient(fixture.MongoDbUrl), options));
            Assert.Contains(nameof(InvalidEmbeddedEntityDbContext), exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(Blog), exception.Message, StringComparison.Ordinal);
            Assert.Contains($"member {nameof(Blog.LastPost)} of", exception.Message, StringComparison.Ordinal);
            Assert.Contains($"member {nameof(Blog.Posts)} of", exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(Post), exception.Message, StringComparison.Ordinal);
            Assert.Contains("reference serializer", exception.Message, StringComparison.Ordinal);
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
        public async Task MissingOriginDocumentDegradesTheSummaryByDefault()
        {
            /* Between a domain delete and its background propagation, the references to the
             * deleted document legitimately dangle: by default a load finding no origin
             * document logs a warning and gives up the summary state, reading the never
             * loaded members at their default values, instead of failing that window with an
             * exception. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var blog = new Blog("blog title");
            await dbContext.Blogs.CreateAsync(blog);
            var bookmark = new Bookmark("my bookmark", blog);
            await dbContext.Bookmarks.CreateAsync(bookmark);
            await DeleteDocumentAsync(dbContext.Blogs.Name, blog.Id!);

            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedBookmark = await dbContext.Bookmarks.FindOneAsync(bookmark.Id);
            var referencedBlog = loadedBookmark.Blog;

            // Action and assert.
            //the denormalized member reads from the summary, without any load
            Assert.Equal("blog title", referencedBlog.Title);

            //the load finding no origin document gives up the summary state, without throwing
            var posts = referencedBlog.Posts;
            Assert.Empty(posts);
            Assert.False(((IReferenceable)referencedBlog).IsSummary);
        }

        [Fact]
        public async Task MissingOriginDocumentDeniesTheLazyLoad()
        {
            /* A referred document deleted from its origin collection is an inconsistency of
             * the database: the summary can't complete its members, and this reference
             * declares the denial of the load, the strict opt-in of a reference that must
             * not tolerate it. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var (blog, post) = await CreateBlogWithPostAsync();
            await DeleteDocumentAsync(dbContext.Posts.Name, post.Id!);

            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedBlog = await dbContext.Blogs.FindOneAsync(blog.Id);
            var referencedPost = loadedBlog.LastPost!;

            // Action and assert.
            //the denormalized member reads from the summary, without any load
            Assert.Equal("post title", referencedPost.Title);

            var exception = Assert.Throws<ScriniumMissingOriginDocumentException>(() => referencedPost.Content);
            Assert.Contains(post.Id!, exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(Post), exception.Message, StringComparison.Ordinal);
            Assert.Contains(dbContext.Posts.Name, exception.Message, StringComparison.Ordinal);

            //the denied load keeps the summary state: the model still requires its origin document
            Assert.True(((IReferenceable)referencedPost).IsSummary);
        }

        [Fact]
        public async Task MissingOriginDocumentDeniesThePreload()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var (blog, post) = await CreateBlogWithPostAsync();
            await DeleteDocumentAsync(dbContext.Posts.Name, post.Id!);

            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedBlog = await dbContext.Blogs.FindOneAsync(blog.Id);

            // Action and assert.
            //the explicit load reports the inconsistency where it happens, not at the first member read
            var exception = await Assert.ThrowsAsync<ScriniumMissingOriginDocumentException>(
                () => dbContext.LoadValuesAsync(loadedBlog.LastPost!, p => p.Content));
            Assert.Contains(post.Id!, exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task MissingOriginDocumentIsToleratedByALaxReference()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var item = new Item("item name");
            await dbContext.Items.CreateAsync(item);

            var review = new Review("review text");
            review.SetItem(item);
            await dbContext.Reviews.CreateAsync(review);

            await DeleteDocumentAsync(dbContext.Items.Name, item.Id!);

            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedReview = await dbContext.Reviews.FindOneAsync(review.Id);
            var referencedItem = loadedReview.Item!;

            // Action.
            var name = referencedItem.Name;

            // Assert.
            //the reference tolerates the missing document: nothing more to load, no exception
            Assert.Null(name);
            Assert.Equal(item.Id, referencedItem.Id);
            Assert.False(((IReferenceable)referencedItem).IsSummary);
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
                    .Set("LastPost._s", "unknown-schema-id")
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

        /* Delete the document alone, without any unit of work bookkeeping: the referencing
         * documents keep their summaries, dangling on a document that doesn't exist anymore. */
        private Task DeleteDocumentAsync(string collectionName, string documentId) =>
            dbContext.Engine.Database.GetCollection<BsonDocument>(collectionName)
                .DeleteOneAsync(Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(documentId)));
    }
}
