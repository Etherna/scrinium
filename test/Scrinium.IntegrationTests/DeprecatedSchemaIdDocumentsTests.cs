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
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.IntegrationTests
{
    [Collection("Integration")]
    public class DeprecatedSchemaIdDocumentsTests : IDisposable
    {
        // Consts.
        private const string CurrentElementName = "_s";
        private const string DeprecatedElementName = "_m";

        // Fields.
        private readonly ITestDbContext dbContext;
        private readonly IntegrationFixture fixture;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public DeprecatedSchemaIdDocumentsTests(IntegrationFixture fixture)
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
        public async Task CountsTheDocumentsCarryingTheSchemaIdWithADeprecatedElementName()
        {
            /* SCR-256: the documents written before the schema id element took its current
             * name carry it under a deprecated one, matched at their root. */

            // Setup.
            /* The collections are shared with the other integration tests: assert on deltas
             * from a baseline, and on the documents created here. */
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var baseline = await dbContext.Blogs.CountDeprecatedSchemaIdDocumentsAsync();

            var legacyBlog = await CreateBlogAsync();
            await WriteWithDeprecatedSchemaIdElementsAsync(legacyBlog.Id);
            await CreateBlogAsync(); //written with the current element name

            // Action.
            var documentsCount = await dbContext.Blogs.CountDeprecatedSchemaIdDocumentsAsync();

            // Assert.
            Assert.Equal(baseline + 1, documentsCount);
        }

        [Fact]
        public async Task MigrationIsDeniedOnReadOnlyRepositories()
        {
            /* A read-only repository denies every write on its collection: the migration fails
             * fast, before scanning anything, while the count is a read and runs. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var readOnlyDbContext = serviceScope.ServiceProvider.GetRequiredService<IReadOnlyDbContext>();

            // Action and assert.
            await readOnlyDbContext.Notes.CountDeprecatedSchemaIdDocumentsAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => readOnlyDbContext.Notes.MigrateDeprecatedSchemaIdDocumentsAsync());
        }

        [Fact]
        public async Task MigrationRewritesTheDocumentsWithTheCurrentElementName()
        {
            /* Renaming the root element wouldn't be enough: the sub-documents and the
             * reference summaries carry their own schema id. The documents are rewritten
             * whole, so every level lands under the current element name, while the documents
             * already written with it stay untouched. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var baseline = await dbContext.Blogs.CountDeprecatedSchemaIdDocumentsAsync();

            var legacyBlog = await CreateBlogAsync();
            await WriteWithDeprecatedSchemaIdElementsAsync(legacyBlog.Id);

            /* A document whose summaries were rewritten by a dependencies update while its
             * root still carried the previous name: the root reports it anyway. */
            var mixedBlog = await CreateBlogAsync();
            await WriteWithDeprecatedSchemaIdElementsAsync(mixedBlog.Id, onlyTheRoot: true);

            /* A document written with the current element name, carrying an element no member
             * maps: a rewrite would drop it, so it tells whether the migration touched it. */
            var currentBlog = await CreateBlogAsync();
            var blogsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("blogs");
            await blogsCollection.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(currentBlog.Id)),
                Builders<BsonDocument>.Update.Set("StrayElement", "kept"));

            // Action.
            var migrationResult = await dbContext.Blogs.MigrateDeprecatedSchemaIdDocumentsAsync();

            // Assert.
            Assert.True(migrationResult.Succeded);
            Assert.Equal(baseline + 2, migrationResult.MigratedDocuments);
            Assert.Equal(0, migrationResult.TotDocumentErrors);
            //the migration repairs every document carrying the previous name, this baseline included
            Assert.Equal(0, await dbContext.Blogs.CountDeprecatedSchemaIdDocumentsAsync());

            //every level of the migrated documents carries the current element name
            foreach (var blogId in new[] { legacyBlog.Id, mixedBlog.Id })
            {
                var rawBlog = await ReadRawBlogAsync(blogId);
                Assert.False(rawBlog.Contains(DeprecatedElementName));
                Assert.Equal(
                    dbContext.Engine.MapRegistry.GetActiveSchemaIdBsonElement(typeof(Blog)).Value.AsString,
                    rawBlog[CurrentElementName].AsString);
                Assert.False(rawBlog["LastPost"].AsBsonDocument.Contains(DeprecatedElementName));
                Assert.True(rawBlog["LastPost"].AsBsonDocument.Contains(CurrentElementName));
                foreach (var rawPost in rawBlog["Posts"].AsBsonArray.Cast<BsonDocument>())
                {
                    Assert.False(rawPost.Contains(DeprecatedElementName));
                    Assert.True(rawPost.Contains(CurrentElementName));
                }
            }

            //the document already written with the current element name is left as it was
            Assert.Equal("kept", (await ReadRawBlogAsync(currentBlog.Id))["StrayElement"].AsString);

            //the migrated document loads with its denormalized summaries, without lazy loading
            using var readScope = fixture.ServiceProvider.CreateScope();
            var readDbContext = readScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedBlog = await readDbContext.Blogs.FindOneAsync(legacyBlog.Id);
            Assert.Equal("blog title", loadedBlog.Title);
            Assert.NotNull(loadedBlog.LastPost);
            Assert.True(readDbContext.IsMemberLoaded(loadedBlog.LastPost, p => p.Title));
            Assert.Equal("second post", loadedBlog.LastPost.Title);
            Assert.Equal(["first post", "second post"], loadedBlog.Posts.Select(p => p.Title));
        }

        // Helpers.
        private async Task<Blog> CreateBlogAsync()
        {
            var blog = new Blog("blog title");
            blog.AddPost(new Post("first post", "content"));
            blog.AddPost(new Post("second post", "content")); //also the last post
            await dbContext.Blogs.CreateAsync(blog);
            return blog;
        }

        private async Task<BsonDocument> ReadRawBlogAsync(string blogId) =>
            await dbContext.Engine.Database.GetCollection<BsonDocument>("blogs")
                .Find(Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(blogId)))
                .SingleAsync();

        /* Rewrite the schema id elements of a stored blog under the previous element name,
         * where a previous version of the application would have written them: the root, and
         * the reference summaries of the document unless the caller asks for the root alone,
         * the shape of a document whose summaries a dependencies update rewrote meanwhile. */
        private async Task WriteWithDeprecatedSchemaIdElementsAsync(string blogId, bool onlyTheRoot = false)
        {
            var rawBlog = await ReadRawBlogAsync(blogId);

            RenameSchemaIdElement(rawBlog);
            if (!onlyTheRoot)
            {
                RenameSchemaIdElement(rawBlog["LastPost"].AsBsonDocument);
                foreach (var rawPost in rawBlog["Posts"].AsBsonArray.Cast<BsonDocument>())
                    RenameSchemaIdElement(rawPost);
            }

            await dbContext.Engine.Database.GetCollection<BsonDocument>("blogs").ReplaceOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(blogId)), rawBlog);

            static void RenameSchemaIdElement(BsonDocument document)
            {
                var elementIndex = document.IndexOfName(CurrentElementName);
                var schemaId = document[CurrentElementName];
                document.RemoveElement(document.GetElement(elementIndex));
                document.InsertAt(elementIndex, new BsonElement(DeprecatedElementName, schemaId));
            }
        }
    }
}
