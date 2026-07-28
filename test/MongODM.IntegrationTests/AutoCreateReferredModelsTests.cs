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
using Etherna.MongODM.IntegrationTests.Fixtures;
using Etherna.MongODM.IntegrationTests.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.MongODM.IntegrationTests
{
    [Collection("Integration")]
    public class AutoCreateReferredModelsTests : IDisposable
    {
        // Fields.
        private readonly ITestDbContext dbContext;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public AutoCreateReferredModelsTests(IntegrationFixture fixture)
        {
            ArgumentNullException.ThrowIfNull(fixture);
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
        public async Task AutoCreatedModelIsChangeTracked()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var item = new Item("tracked item");
            var review = item.AddReview("first text");
            await dbContext.Items.CreateAsync(item);

            // Action.
            review.Text = "updated text";
            await dbContext.SaveChangesAsync();

            // Assert.
            //the auto created model joined the unit of work: its later changes save normally
            var reviewsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("reviews");
            var rawReview = await reviewsCollection.Find(
                Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(review.Id))).SingleAsync();
            Assert.Equal("updated text", rawReview["Text"].AsString);
        }

        [Fact]
        public async Task CreateAutoCreatesNewReferredModelsWithCircularReferences()
        {
            /* A domain method creates and links a new model, holding also a back reference
             * to its still uncreated owner. Both models are new and reference each other:
             * the ids assigned upfront let both references serialize complete. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var item = new Item("item name");
            var review = item.AddReview("review text");

            // Action.
            await dbContext.Items.CreateAsync(item);

            // Assert.
            Assert.NotNull(item.Id);
            Assert.NotNull(review.Id);

            //the new referred model persisted into its repository, with the back reference complete
            var reviewsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("reviews");
            var rawReview = await reviewsCollection.Find(
                Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(review.Id))).SingleAsync();
            Assert.Equal(ObjectId.Parse(item.Id), rawReview["Item"]["_id"].AsObjectId);

            //the referencing document serializes the reference complete
            var itemsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("items");
            var rawItem = await itemsCollection.Find(
                Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(item.Id))).SingleAsync();
            Assert.Equal(ObjectId.Parse(review.Id), rawItem["Reviews"][0]["_id"].AsObjectId);
            Assert.Equal("review text", rawItem["Reviews"][0]["Text"].AsString);

            //the persisted models load back with their mutual references
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var foundReview = await dbContext.Reviews.FindOneAsync(review.Id);
            Assert.Equal("review text", foundReview.Text);
            Assert.Equal(item.Id, foundReview.Item!.Id);
        }

        [Fact]
        public async Task CreateAutoCreatesTransitiveNewReferredModels()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("post title", "post content");
            var blog = new Blog("blog title");
            blog.AddPost(post);
            var bookmark = new Bookmark("bookmark label", blog);

            // Action.
            await dbContext.Bookmarks.CreateAsync(bookmark);

            // Assert.
            Assert.NotNull(blog.Id);
            Assert.NotNull(post.Id);

            //the whole chain of new models persisted, with complete references at every level
            var bookmarksCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("bookmarks");
            var rawBookmark = await bookmarksCollection.Find(
                Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(bookmark.Id))).SingleAsync();
            Assert.Equal(ObjectId.Parse(blog.Id), rawBookmark["Blog"]["_id"].AsObjectId);

            var blogsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("blogs");
            var rawBlog = await blogsCollection.Find(
                Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(blog.Id))).SingleAsync();
            Assert.Equal(ObjectId.Parse(post.Id), rawBlog["LastPost"]["_id"].AsObjectId);

            var postsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("posts");
            var rawPost = await postsCollection.Find(
                Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(post.Id))).SingleAsync();
            Assert.Equal("post content", rawPost["Content"].AsString);
        }

        [Fact]
        public async Task ExistingReferredModelIsNotRecreatedOnCreate()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var review = new Review("existing review");
            await dbContext.Reviews.CreateAsync(review);
            var reviewId = review.Id;

            var item = new Item("item name");
            item.AddReview(review);

            // Action.
            await dbContext.Items.CreateAsync(item);

            // Assert.
            //the existing model keeps its identity, without a second document
            Assert.Equal(reviewId, review.Id);
            var reviewsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("reviews");
            Assert.Equal(1, await reviewsCollection.CountDocumentsAsync(
                Builders<BsonDocument>.Filter.Eq("Text", "existing review")));

            //the reference serializes with the existing id, and the back link update persists
            var itemsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("items");
            var rawItem = await itemsCollection.Find(
                Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(item.Id))).SingleAsync();
            Assert.Equal(ObjectId.Parse(reviewId!), rawItem["Reviews"][0]["_id"].AsObjectId);

            var rawReview = await reviewsCollection.Find(
                Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(reviewId))).SingleAsync();
            Assert.Equal(ObjectId.Parse(item.Id), rawReview["Item"]["_id"].AsObjectId);
        }

        [Fact]
        public async Task SaveChangesAutoCreatesNewReferredModel()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var item = new Item("item name");
            await dbContext.Items.CreateAsync(item);

            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedItem = await dbContext.Items.FindOneAsync(item.Id);
            var review = loadedItem.AddReview("late review");

            // Action.
            await dbContext.SaveChangesAsync();

            // Assert.
            Assert.NotNull(review.Id);

            //the new model persisted with its back reference
            var reviewsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("reviews");
            var rawReview = await reviewsCollection.Find(
                Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(review.Id))).SingleAsync();
            Assert.Equal(ObjectId.Parse(item.Id), rawReview["Item"]["_id"].AsObjectId);

            //the changed member update carries the complete reference
            var itemsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("items");
            var rawItem = await itemsCollection.Find(
                Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(item.Id))).SingleAsync();
            Assert.Equal(ObjectId.Parse(review.Id), rawItem["Reviews"][0]["_id"].AsObjectId);
            Assert.Equal("late review", rawItem["Reviews"][0]["Text"].AsString);
        }

        [Fact]
        public async Task SaveChangesCreatesOnlyTheNewReferredModels()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var existingReview = new Review("mixed existing");
            await dbContext.Reviews.CreateAsync(existingReview);
            var item = new Item("item name");
            await dbContext.Items.CreateAsync(item);

            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedItem = await dbContext.Items.FindOneAsync(item.Id);
            loadedItem.AddReview(existingReview);
            var newReview = loadedItem.AddReview("mixed new");

            // Action.
            await dbContext.SaveChangesAsync();

            // Assert.
            //only the new model is created, the existing one keeps its single document
            Assert.NotNull(newReview.Id);
            var reviewsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("reviews");
            Assert.Equal(1, await reviewsCollection.CountDocumentsAsync(
                Builders<BsonDocument>.Filter.Eq("Text", "mixed existing")));
            Assert.Equal(1, await reviewsCollection.CountDocumentsAsync(
                Builders<BsonDocument>.Filter.Eq("Text", "mixed new")));

            //both references serialize complete on the referencing document
            var itemsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("items");
            var rawItem = await itemsCollection.Find(
                Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(item.Id))).SingleAsync();
            Assert.Equal(ObjectId.Parse(existingReview.Id), rawItem["Reviews"][0]["_id"].AsObjectId);
            Assert.Equal(ObjectId.Parse(newReview.Id), rawItem["Reviews"][1]["_id"].AsObjectId);
        }

        [Fact]
        public async Task UpsertingReferenceToNewModelWithoutIdThrows()
        {
            /* The upsert apis serialize their on insert model directly, without the new
             * referred models auto creation: a reference to a model without id fails loudly
             * instead of persisting a null id reference, lost at the next load. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var item = new Item("upsert item");
            item.AddReview("upsert review");

            // Action.
            /* The driver wraps the member serialization failure, keeping the reason in the
             * message and the original exception as inner. */
            var exception = await Assert.ThrowsAsync<BsonSerializationException>(() =>
                dbContext.Items.UpsertSetFieldAsync(
                    i => i.Name == "upsert item",
                    i => i.Name,
                    "renamed item",
                    item));

            // Assert.
            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Contains("without id", exception.Message, StringComparison.Ordinal);
        }
    }
}
