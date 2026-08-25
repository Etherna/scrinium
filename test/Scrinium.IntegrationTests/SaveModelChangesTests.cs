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
using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Etherna.Scrinium.Core.ProxyModels;
using Etherna.Scrinium.IntegrationTests.Fixtures;
using Etherna.Scrinium.IntegrationTests.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.IntegrationTests
{
    [Collection("Integration")]
    public class SaveModelChangesTests : IDisposable
    {
        // Fields.
        private readonly ITestDbContext dbContext;
        private readonly IntegrationFixture fixture;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public SaveModelChangesTests(IntegrationFixture fixture)
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
        public async Task SaveChangesUpdatesOnlyChangedMembersAndRefreshesModel()
        {
            /* Concurrent changes to disjoint members of the same document must all survive:
             * the save updates only the changed members, and refreshes the saved model with
             * the returned document state, including the concurrent changes. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("original title", "original content");
            await dbContext.Posts.CreateAsync(post);

            var loadedPost = await dbContext.Posts.FindOneAsync(post.Id);
            loadedPost.Content = "content from A";

            //concurrent update of a different member from another scope
            using (var externalScope = fixture.ServiceProvider.CreateScope())
            {
                var externalDbContext = externalScope.ServiceProvider.GetRequiredService<ITestDbContext>();
                var externalPost = await externalDbContext.Posts.FindOneAsync(post.Id);
                externalPost.Title = "title from B";
                await externalDbContext.SaveChangesAsync();
            }

            // Action.
            await dbContext.SaveChangesAsync();

            // Assert.
            //the saved model is refreshed with the concurrent change
            Assert.Equal("title from B", loadedPost.Title);
            Assert.Equal("content from A", loadedPost.Content);

            //both changes are merged on db
            using var readScope = fixture.ServiceProvider.CreateScope();
            var readDbContext = readScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var foundPost = await readDbContext.Posts.FindOneAsync(post.Id);
            Assert.Equal("title from B", foundPost.Title);
            Assert.Equal("content from A", foundPost.Content);
        }

        [Fact]
        public async Task SaveChangesReplacesDocumentsNotOnActiveSchema()
        {
            /* Documents serialized with a not active schema can't receive member level
             * updates, or members of different schemas would mix into a broken document:
             * the save falls back to a whole document replace, migrating them. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("title", "content");
            await dbContext.Posts.CreateAsync(post);

            //simulate a document serialized with an old schema
            var postsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("posts");
            var postFilter = Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(post.Id));
            await postsCollection.UpdateOneAsync(postFilter,
                Builders<BsonDocument>.Update.Set("_s", "legacy-schema-id"));

            // Action.
            var loadedPost = await dbContext.Posts.FindOneAsync(post.Id);
            loadedPost.Content = "updated content";
            await dbContext.SaveChangesAsync();

            // Assert.
            //the document has been migrated to the active schema, with the change persisted
            var rawPost = await postsCollection.Find(postFilter).SingleAsync();
            Assert.NotEqual("legacy-schema-id", rawPost["_s"].AsString);
            Assert.Equal("updated content", rawPost["Content"].AsString);
            Assert.Equal("title", rawPost["Title"].AsString);
        }

        [Fact]
        public async Task SavingChangedSummaryUpdatesOnlyItsChangesAndUpgradesIt()
        {
            /* Saving a changed summary reference updates only its changed members, without
             * serializing (and lazy loading) the whole document. The refresh with the
             * returned document state upgrades the summary to a full model. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("post title", "post content");
            await dbContext.Posts.CreateAsync(post);
            var blog = new Blog("blog title");
            blog.AddPost(post);
            await dbContext.Blogs.CreateAsync(blog);

            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedBlog = await dbContext.Blogs.FindOneAsync(blog.Id);
            var referencedPost = loadedBlog.LastPost!;
            Assert.True(((IReferenceable)referencedPost).IsSummary);

            // Action.
            referencedPost.Title = "updated title";
            await dbContext.SaveChangesAsync();

            // Assert.
            //the refresh upgraded the summary with the whole document state
            Assert.False(((IReferenceable)referencedPost).IsSummary);
            Assert.Equal("post content", referencedPost.Content);

            //on db, the change is persisted and the not loaded members are intact
            using var readScope = fixture.ServiceProvider.CreateScope();
            var readDbContext = readScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var foundPost = await readDbContext.Posts.FindOneAsync(post.Id);
            Assert.Equal("updated title", foundPost.Title);
            Assert.Equal("post content", foundPost.Content);
        }

        [Fact]
        public async Task SaveWithDocumentReplaceOptionKeepsWholeDocumentSemantics()
        {
            /* Repositories opting into SaveWithDocumentReplace persist changed models
             * replacing the whole document: concurrent changes to other members are
             * overwritten by the saved model state, last writer wins on the document. */

            // Setup.
            using var scope = fixture.ServiceProvider.CreateScope();
            var secondDbContext = scope.ServiceProvider.GetRequiredService<ISecondDbContext>();
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            var note = new Note("original text") { Tag = "original tag" };
            await secondDbContext.Notes.CreateAsync(note);

            var loadedNote = await secondDbContext.Notes.FindOneAsync(note.Id);
            loadedNote.Text = "text from A";

            //concurrent update of a different member from another scope
            using (var externalScope = fixture.ServiceProvider.CreateScope())
            {
                var externalDbContext = externalScope.ServiceProvider.GetRequiredService<ISecondDbContext>();
                var externalNote = await externalDbContext.Notes.FindOneAsync(note.Id);
                externalNote.Tag = "tag from B";
                await externalDbContext.SaveChangesAsync();
            }

            // Action.
            await secondDbContext.SaveChangesAsync();

            // Assert.
            //whole document semantics: the concurrent member change is overwritten
            using var readScope = fixture.ServiceProvider.CreateScope();
            var readDbContext = readScope.ServiceProvider.GetRequiredService<ISecondDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var foundNote = await readDbContext.Notes.FindOneAsync(note.Id);
            Assert.Equal("text from A", foundNote.Text);
            Assert.Equal("original tag", foundNote.Tag);
        }
    }
}
