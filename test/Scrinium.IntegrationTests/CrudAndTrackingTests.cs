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
using Etherna.MongoDB.Driver.Linq;
using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Etherna.Scrinium.IntegrationTests.Fixtures;
using Etherna.Scrinium.IntegrationTests.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.IntegrationTests
{
    [Collection("Integration")]
    public class CrudAndTrackingTests : IDisposable
    {
        // Fields.
        private readonly ITestDbContext dbContext;
        private readonly IntegrationFixture fixture;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public CrudAndTrackingTests(IntegrationFixture fixture)
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
        public async Task FindOneReadsThroughLoadedFullInstances()
        {
            /* A full instance already loaded on the scope satisfies FindOneAsync without a db
             * round trip: proved by deleting the document behind the scenes, where a db read
             * would fail. Inside its scope, the unit of work is the source of truth. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("title", "content");
            await dbContext.Posts.CreateAsync(post);

            var loadedPost = await dbContext.Posts.FindOneAsync(post.Id);

            //delete the document behind the scenes
            var postsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("posts");
            await postsCollection.DeleteOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(post.Id)));

            // Action.
            var foundPost = await dbContext.Posts.FindOneAsync(post.Id);

            // Assert.
            Assert.Same(loadedPost, foundPost);
        }

        [Fact]
        public async Task FindOneReturnsSameInstanceInScopeAndFreshDataInNewScope()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("title", "content");
            await dbContext.Posts.CreateAsync(post);

            var firstLoadedPost = await dbContext.Posts.FindOneAsync(post.Id);

            //simulate a concurrent update from a different DI scope
            using (var externalScope = fixture.ServiceProvider.CreateScope())
            {
                var externalDbContext = externalScope.ServiceProvider.GetRequiredService<ITestDbContext>();
                var externalPost = await externalDbContext.Posts.FindOneAsync(post.Id);
                externalPost.Content = "updated externally";
                await externalDbContext.SaveChangesAsync();
            }

            // Action.
            var secondLoadedPost = await dbContext.Posts.FindOneAsync(post.Id);

            // Assert.
            //identity map: the same scope keeps returning the loaded instance, with its state
            Assert.Same(firstLoadedPost, secondLoadedPost);
            Assert.Equal("content", secondLoadedPost.Content);

            //a new scope reads fresh data on a new instance
            using var freshScope = fixture.ServiceProvider.CreateScope();
            var freshDbContext = freshScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            var freshLoadedPost = await freshDbContext.Posts.FindOneAsync(post.Id);
            Assert.NotSame(secondLoadedPost, freshLoadedPost);
            Assert.Equal("updated externally", freshLoadedPost.Content);
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
            using (dbContext.Engine.SerializerModifierAccessor.EnableCacheSerializerModifier(noCache: true))
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

            //no cache: loads don't register instances, nor deduplicate between them
            var post2 = new Post("title 2", "content 2");
            await dbContext.Posts.CreateAsync(post2);
            using (dbContext.Engine.SerializerModifierAccessor.EnableCacheSerializerModifier(noCache: true))
            {
                var firstNoCachePost = await dbContext.Posts.FindOneAsync(post2.Id);
                var secondNoCachePost = await dbContext.Posts.FindOneAsync(post2.Id);
                Assert.NotSame(firstNoCachePost, secondNoCachePost);
            }
            Assert.Null(dbContext.TryGetLoadedModel(dbContext.Posts, post2.Id!));
        }

        [Fact]
        public async Task SameInstanceForSameDocumentInSameScope()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("title", "content");
            await dbContext.Posts.CreateAsync(post);

            // Action.
            var firstLoadedPost = await dbContext.Posts.FindOneAsync(post.Id);
            var secondLoadedPost = await dbContext.Posts.FindOneAsync(post.Id);

            // Assert.
            //identity map: one document, one instance inside the scope
            Assert.Same(firstLoadedPost, secondLoadedPost);
            Assert.Same(firstLoadedPost, dbContext.TryGetLoadedModel(dbContext.Posts, post.Id!));
        }

        [Fact]
        public async Task UpsertWithNestedUpdatedFieldsInsertsTheDocumentAnInsertWrites()
        {
            /* The on insert instructions can't set whole an element that an updated field
             * navigates into, or the server refuses the update as a conflict: the element
             * splits into its sub elements, so the document created by the upsert keeps the
             * siblings of the nested updated fields, the ones MongODM writes included. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var talliesCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("tallies");
            var insertedTally = new Tally(
                "song",
                new Dictionary<string, int> { ["plays"] = 1, ["likes"] = 1, ["shares"] = 4 },
                new Score(10, "song score"));
            await dbContext.Tallies.CreateAsync(insertedTally);
            var rawInsertedTally = await talliesCollection.Find(
                Builders<BsonDocument>.Filter.Eq("Subject", "song")).SingleAsync();
            await dbContext.Tallies.DeleteAsync(insertedTally);

            // Action.
            /* The on insert model is the document to insert when absent, so it carries the
             * values the update applies: two counters of the same dictionary, and a member of
             * the embedded score. */
            var oldDocument = await dbContext.Tallies.UpsertAsync(
                Builders<Tally>.Filter.Eq(t => t.Subject, "song"),
                Builders<Tally>.Update.Combine(
                    Builders<Tally>.Update.Inc(t => t.Counters["plays"], 1),
                    Builders<Tally>.Update.Inc(t => t.Counters["likes"], 1),
                    Builders<Tally>.Update.Set(t => t.Score.Total, 10)),
                new Tally(
                    "song",
                    new Dictionary<string, int> { ["plays"] = 1, ["likes"] = 1, ["shares"] = 4 },
                    new Score(10, "song score")),
                [
                    new ExpressionFieldDefinition<Tally, int>(t => t.Counters["plays"]),
                    new ExpressionFieldDefinition<Tally, int>(t => t.Counters["likes"]),
                    new ExpressionFieldDefinition<Tally, int>(t => t.Score.Total)
                ]);

            // Assert.
            Assert.Null(oldDocument);
            var rawUpsertedTally = await talliesCollection.Find(
                Builders<BsonDocument>.Filter.Eq("Subject", "song")).SingleAsync();
            /* The upsert creates the same document an insert writes, but its identity. The
             * server writes the update fields in its own order, so the elements compare
             * sorted by name at every level. */
            rawInsertedTally.Remove("_id");
            rawUpsertedTally.Remove("_id");
            Assert.Equal(SortElements(rawInsertedTally), SortElements(rawUpsertedTally));
        }

        // Helpers.
        private static BsonDocument SortElements(BsonDocument document) =>
            new(document.Elements
                .OrderBy(element => element.Name, StringComparer.Ordinal)
                .Select(element => element.Value is BsonDocument subDocument ?
                    new BsonElement(element.Name, SortElements(subDocument)) :
                    element));
    }
}
