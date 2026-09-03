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
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.IntegrationTests
{
    [Collection("Integration")]
    public class DocumentCountsTests : IDisposable
    {
        // Fields.
        private readonly ITestDbContext dbContext;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public DocumentCountsTests(IntegrationFixture fixture)
        {
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
        public async Task CountsDocumentsGroupedBySchemaId()
        {
            /* SCR-204: a single server side scan groups the collection documents by their
             * schema id, read from the current element name or from the deprecated one.
             * Schema ids not registered on the db context report too, and documents without
             * any schema id element count aside: both identify documents needing attention. */

            // Setup.
            /* The collection is shared with the other integration tests: assert on count
             * deltas from a baseline, with unique values for the injected schema ids. */
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var (baselineCounts, baselineWithoutSchemaId) = await dbContext.Posts.CountDocumentsBySchemaIdAsync();

            var posts = new[]
            {
                new Post("title", "content"), //keeps the active schema id
                new Post("title", "content"), //rewritten under the deprecated element name
                new Post("title", "content"), //rewritten with a not registered schema id
                new Post("title", "content")  //rewritten without any schema id element
            };
            await dbContext.Posts.CreateAsync(posts);

            var postsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("posts");
            var activeSchemaId = (await postsCollection
                .Find(Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(posts[0].Id)))
                .SingleAsync())["_s"].AsString;
            var legacySchemaId = "legacy-" + Guid.NewGuid().ToString("N");

            await postsCollection.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(posts[1].Id)),
                Builders<BsonDocument>.Update.Rename("_s", "_m"));
            await postsCollection.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(posts[2].Id)),
                Builders<BsonDocument>.Update.Set("_s", legacySchemaId));
            await postsCollection.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(posts[3].Id)),
                Builders<BsonDocument>.Update.Unset("_s"));

            // Action.
            var (counts, withoutSchemaId) = await dbContext.Posts.CountDocumentsBySchemaIdAsync();

            // Assert.
            //the deprecated element name document groups with the active schema id ones
            Assert.Equal(
                baselineCounts.GetValueOrDefault(activeSchemaId) + 2,
                counts[activeSchemaId]);
            Assert.Equal(1, counts[legacySchemaId]);
            Assert.Equal(baselineWithoutSchemaId + 1, withoutSchemaId);
        }

        [Fact]
        public async Task EstimatesTheCollectionDocumentsCount()
        {
            /* The estimated count reads the collection metadata, without scanning documents:
             * it sizes a collection before asking for the schema ids scan. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var baseline = await dbContext.Posts.EstimatedDocumentCountAsync();

            await dbContext.Posts.CreateAsync([
                new Post("title", "content"),
                new Post("title", "content")
            ]);

            // Action.
            var estimatedCount = await dbContext.Posts.EstimatedDocumentCountAsync();

            // Assert.
            Assert.Equal(baseline + 2, estimatedCount);
        }

        [Fact]
        public async Task ReadsSchemaIdFromCurrentElementNameFirst()
        {
            /* A document carrying both the current and a fallback schema id element counts
             * on the current element value, like the read path resolves it. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("title", "content");
            await dbContext.Posts.CreateAsync(post);

            var ghostSchemaId = "ghost-" + Guid.NewGuid().ToString("N");
            var postsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("posts");
            await postsCollection.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(post.Id)),
                Builders<BsonDocument>.Update.Set("_m", ghostSchemaId));

            // Action.
            var (counts, _) = await dbContext.Posts.CountDocumentsBySchemaIdAsync();

            // Assert.
            Assert.DoesNotContain(ghostSchemaId, counts.Keys);
        }
    }
}
