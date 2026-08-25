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
using Etherna.Scrinium.IntegrationTests.Fixtures;
using Etherna.Scrinium.IntegrationTests.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.IntegrationTests
{
    [Collection("Integration")]
    public class SchemaIdElementCompatibilityTests : IDisposable
    {
        // Fields.
        private readonly ITestDbContext dbContext;
        private readonly IntegrationFixture fixture;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public SchemaIdElementCompatibilityTests(IntegrationFixture fixture)
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
        public async Task LoadsDocumentsCarryingTheDeprecatedSchemaIdElementName()
        {
            /* MODM-153: the schema id element name is "_s"; documents written with the
             * previous "_m" name keep loading through the deprecated element name,
             * without reporting the recognized element into extra elements. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("title", "content");
            await dbContext.Posts.CreateAsync(post);

            //rewrite the document schema id under the previous element name
            var postsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("posts");
            var postFilter = Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(post.Id));
            await postsCollection.UpdateOneAsync(postFilter,
                Builders<BsonDocument>.Update.Rename("_s", "_m"));

            // Action.
            using var readScope = fixture.ServiceProvider.CreateScope();
            var readDbContext = readScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedPost = await readDbContext.Posts.FindOneAsync(post.Id);

            // Assert.
            Assert.Equal("title", loadedPost.Title);
            Assert.Equal("content", loadedPost.Content);
            Assert.True(loadedPost.ExtraElements is null || loadedPost.ExtraElements.Count == 0);
        }

        [Fact]
        public async Task LoadsReferenceSummariesCarryingTheDeprecatedSchemaIdElementName()
        {
            /* A reference sub-document resolves its summary schema through the deprecated
             * element name too: without recognizing the element, only the reference id
             * would deserialize, discarding the denormalized summary members. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("post title", "post content");
            await dbContext.Posts.CreateAsync(post);
            var blog = new Blog("blog title");
            blog.AddPost(post);
            await dbContext.Blogs.CreateAsync(blog);

            //rewrite the summary schema id under the previous element name
            var blogsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("blogs");
            var blogFilter = Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(blog.Id));
            await blogsCollection.UpdateOneAsync(blogFilter,
                Builders<BsonDocument>.Update.Rename("LastPost._s", "LastPost._m"));

            // Action.
            using var readScope = fixture.ServiceProvider.CreateScope();
            var readDbContext = readScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedBlog = await readDbContext.Blogs.FindOneAsync(blog.Id);

            // Assert.
            //the summary member deserialized from the sub-document, without lazy loading
            Assert.NotNull(loadedBlog.LastPost);
            Assert.True(readDbContext.IsMemberLoaded(loadedBlog.LastPost, p => p.Title));
            Assert.Equal("post title", loadedBlog.LastPost.Title);
        }

        [Fact]
        public async Task SavingChangesMigratesDocumentsToTheCurrentSchemaIdElementName()
        {
            /* A document carrying its schema id under the deprecated element name can't
             * match the member level update guard on the current name: the save falls back
             * to a whole document replace, migrating the document to the current name. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("title", "content");
            await dbContext.Posts.CreateAsync(post);

            var postsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("posts");
            var postFilter = Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(post.Id));
            var rawPost = await postsCollection.Find(postFilter).SingleAsync();
            var activeSchemaId = rawPost["_s"].AsString;
            await postsCollection.UpdateOneAsync(postFilter,
                Builders<BsonDocument>.Update.Rename("_s", "_m"));

            // Action.
            using var workScope = fixture.ServiceProvider.CreateScope();
            var workDbContext = workScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedPost = await workDbContext.Posts.FindOneAsync(post.Id);
            loadedPost.Content = "updated content";
            await workDbContext.SaveChangesAsync();

            // Assert.
            //the document carries the current element name, with the change persisted
            rawPost = await postsCollection.Find(postFilter).SingleAsync();
            Assert.Equal(activeSchemaId, rawPost["_s"].AsString);
            Assert.False(rawPost.Contains("_m"));
            Assert.Equal("updated content", rawPost["Content"].AsString);
            Assert.Equal("title", rawPost["Title"].AsString);
        }
    }
}
