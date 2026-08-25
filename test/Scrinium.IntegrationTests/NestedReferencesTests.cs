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
    public class NestedReferencesTests : IDisposable
    {
        // Fields.
        private readonly ITestDbContext dbContext;
        private readonly IntegrationFixture fixture;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public NestedReferencesTests(IntegrationFixture fixture)
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
        public async Task ChangedInnerReferencedModelRefreshesNestedSummaries()
        {
            /* A summary can denormalize another reference among its members: a change of
             * the inner referenced model refreshes the nested sub-document, reserialized
             * with the nested reference serializer at its composed element path. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            fixture.TaskRunner.ClearPending();
            var (bookmark, _, post) = await CreateBookmarkGraphAsync();

            // Action: update the inner referenced post, and execute the enqueued task.
            var loadedPost = await dbContext.Posts.FindOneAsync(post.Id);
            loadedPost.Title = "updated title";
            await dbContext.SaveChangesAsync();
            await fixture.TaskRunner.ExecutePendingAsync(fixture.ServiceProvider);

            // Assert.
            var rawNestedPost = (await GetRawBookmarkAsync(bookmark.Id))["Blog"]["LastPost"].AsBsonDocument;
            Assert.Equal("updated title", rawNestedPost["Title"].AsString);
            Assert.Equal("8fa8f258-70b2-464f-8b57-11de27ca0b81", rawNestedPost["_s"].AsString);
            Assert.False(rawNestedPost.Contains("Content")); //summaries keep only their summary members
        }

        [Fact]
        public async Task NestedSummaryKeepsTheReferenceShape()
        {
            /* The nested reference persists inside the hosting summary with its own
             * reference schema shape: schema id, reference id, and summary members only. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var (bookmark, blog, post) = await CreateBookmarkGraphAsync();

            // Assert.
            var rawBlogSummary = (await GetRawBookmarkAsync(bookmark.Id))["Blog"].AsBsonDocument;
            Assert.Equal("9c1d4e7f-2b8a-4c6d-b3e9-7a5f0d8c2b4e", rawBlogSummary["_s"].AsString);
            Assert.Equal(ObjectId.Parse(blog.Id), rawBlogSummary["_id"].AsObjectId);
            Assert.Equal("blog title", rawBlogSummary["Title"].AsString);
            Assert.False(rawBlogSummary.Contains("Posts")); //not a summary member

            var rawNestedPost = rawBlogSummary["LastPost"].AsBsonDocument;
            Assert.Equal("8fa8f258-70b2-464f-8b57-11de27ca0b81", rawNestedPost["_s"].AsString);
            Assert.Equal(ObjectId.Parse(post.Id), rawNestedPost["_id"].AsObjectId);
            Assert.Equal("post title", rawNestedPost["Title"].AsString);
            Assert.False(rawNestedPost.Contains("Content")); //not a summary member
        }

        [Fact]
        public async Task NestedSummaryLazyLoadsFromItsSourceRepository()
        {
            /* The nested summary instance binds to its own source repository: reads of
             * its summary members are free, a read of a missing member loads the full
             * document, and the identity map keeps one instance for its document. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var (bookmark, _, post) = await CreateBookmarkGraphAsync();

            using var workScope = fixture.ServiceProvider.CreateScope();
            var workDbContext = workScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            var loadedBookmark = await workDbContext.Bookmarks.FindOneAsync(bookmark.Id);
            var blogSummary = loadedBookmark.Blog;
            var nestedPost = blogSummary.LastPost!;

            // Assert: both levels deserialize as summaries, with the nested members loaded.
            Assert.True(((IReferenceable)blogSummary).IsSummary);
            Assert.True(((IReferenceable)nestedPost).IsSummary);
            Assert.True(workDbContext.IsMemberLoaded(nestedPost, p => p.Title));
            Assert.Equal("post title", nestedPost.Title);

            // Action: a missing member read loads the full document from the posts repository.
            var content = nestedPost.Content;

            // Assert.
            Assert.Equal("content", content);
            Assert.False(((IReferenceable)nestedPost).IsSummary);

            //one document, one instance on the scope
            var foundPost = await workDbContext.Posts.FindOneAsync(post.Id);
            Assert.Same(nestedPost, foundPost);
        }

        [Fact]
        public void OwnerEntityIdMapResolvesOnlyForEntitySchemaLevels()
        {
            /* The owner entity id of a member map is the id sibling sharing its schema:
             * members of schemas above the entity levels (e.g. the base object maps,
             * without an id of their own) resolve no owner id, at any nesting depth. */

            // Setup.
            var bookmarkMemberMaps = dbContext.Engine.MapRegistry.MemberMapsById.Values
                .Where(mm => mm.MemberMapPath.First().ModelMapSchema.ModelMap.ModelType == typeof(Bookmark))
                .ToArray();

            // Assert: a nested entity level member resolves the id sibling of its own schema.
            var nestedTitleMap = bookmarkMemberMaps.Single(mm =>
                mm.RenderElementPath(false, _ => ".$", _ => ".*") == "Blog.LastPost.Title");
            var ownerIdMap = nestedTitleMap.OwnerEntityIdMap;
            Assert.NotNull(ownerIdMap);
            Assert.True(ownerIdMap.IsIdMember);
            Assert.Same(nestedTitleMap.ModelMapSchema, ownerIdMap.ModelMapSchema);
            Assert.Same(nestedTitleMap.ParentMemberMap, ownerIdMap.ParentMemberMap);

            // Assert: nested members of not entity schema levels resolve no owner id.
            var baseLevelMemberMaps = bookmarkMemberMaps
                .Where(mm => mm.RenderElementPath(false, _ => ".$", _ => ".*") == "Blog.LastPost.ExtraElements" &&
                    !mm.ModelMapSchema.IsEntity)
                .ToArray();
            Assert.NotEmpty(baseLevelMemberMaps);
            Assert.All(baseLevelMemberMaps, mm => Assert.Null(mm.OwnerEntityIdMap));
        }

        // Helpers.
        /* Create a post, a blog referencing it as last post, and a bookmark referencing
         * the blog with a summary that denormalizes the nested last post reference. */
        private async Task<(Bookmark bookmark, Blog blog, Post post)> CreateBookmarkGraphAsync()
        {
            var post = new Post("post title", "content");
            await dbContext.Posts.CreateAsync(post);

            var blog = new Blog("blog title");
            blog.AddPost(post);
            await dbContext.Blogs.CreateAsync(blog);

            var bookmark = new Bookmark("my bookmark", blog);
            await dbContext.Bookmarks.CreateAsync(bookmark);

            return (bookmark, blog, post);
        }

        private async Task<BsonDocument> GetRawBookmarkAsync(string bookmarkId)
        {
            var bookmarksCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("bookmarks");
            return await bookmarksCollection
                .Find(Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(bookmarkId)))
                .SingleAsync();
        }
    }
}
