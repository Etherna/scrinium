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
using Etherna.Scrinium.Core.Tasks;
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
    public class UpdateDocDependenciesTests : IDisposable
    {
        // Fields.
        private readonly ITestDbContext dbContext;
        private readonly IntegrationFixture fixture;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public UpdateDocDependenciesTests(IntegrationFixture fixture)
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
        public async Task ChangeOfANotSummarizedMemberLeavesSummariesUntouched()
        {
            /* A member not denormalized by any reference summary produces no summary
             * member maps to refresh: no update task is enqueued, and the referencing
             * documents stay untouched. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            fixture.TaskRunner.ClearPending();

            var post = new Post("title", "content");
            await dbContext.Posts.CreateAsync(post);
            var blog = new Blog("my blog");
            blog.AddPost(post);
            await dbContext.Blogs.CreateAsync(blog);

            var blogsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("blogs");
            var rawBlogBefore = await blogsCollection.Find(IdFilter(blog.Id)).SingleAsync();

            // Action: update a member out of every summary.
            var loadedPost = await dbContext.Posts.FindOneAsync(post.Id);
            loadedPost.Content = "updated content";
            await dbContext.SaveChangesAsync();

            // Assert.
            Assert.Equal(0, fixture.TaskRunner.PendingCount);

            var rawBlogAfter = await blogsCollection.Find(IdFilter(blog.Id)).SingleAsync();
            Assert.Equal(rawBlogBefore, rawBlogAfter);
        }

        [Fact]
        public async Task ChangedReferencedModelUpdatesSummariesOnDependentDocuments()
        {
            /* Pin the whole summary maintenance flow: replacing a referenced model enqueues an
             * update task from DbMaintainer.OnUpdatedModel, carrying the repository that
             * performed the write. Its execution refreshes the denormalized sub-documents
             * embedded by dependent documents. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            fixture.TaskRunner.ClearPending();

            var post = new Post("original title", "content");
            await dbContext.Posts.CreateAsync(post);

            var blog = new Blog("my blog");
            blog.AddPost(post);
            await dbContext.Blogs.CreateAsync(blog);

            // Action: update the referenced post through its repository.
            var loadedPost = await dbContext.Posts.FindOneAsync(post.Id);
            loadedPost.Title = "updated title";
            await dbContext.SaveChangesAsync();

            // Assert: the persisted summary is refreshed only by the enqueued task execution.
            var blogsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("blogs");
            var blogFilter = Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(blog.Id));

            var rawBlog = await blogsCollection.Find(blogFilter).SingleAsync();
            Assert.Equal("original title", rawBlog["LastPost"]["Title"].AsString);

            await fixture.TaskRunner.ExecutePendingAsync(fixture.ServiceProvider);

            rawBlog = await blogsCollection.Find(blogFilter).SingleAsync();
            Assert.Equal("updated title", rawBlog["LastPost"]["Title"].AsString);

            //the refreshed summaries keep the reference schema shape, on plain and array members
            var rawLastPost = rawBlog["LastPost"].AsBsonDocument;
            Assert.Equal("8fa8f258-70b2-464f-8b57-11de27ca0b81", rawLastPost["_s"].AsString);
            Assert.False(rawLastPost.Contains("Content")); //summaries keep only their summary members

            var rawReferencedPost = rawBlog["Posts"].AsBsonArray[0].AsBsonDocument;
            Assert.Equal("e7d1fe44-c5d7-4e5b-8ab6-898295619131", rawReferencedPost["_s"].AsString);
            Assert.False(rawReferencedPost.Contains("Title")); //id only reference
        }

        [Fact]
        public async Task ChangedReferencedModelUpdatesSummariesOnEveryHostingCollection()
        {
            /* Two repositories of the db context host the dependent model type: the update
             * task fans out to every collection that can host referencing documents,
             * refreshing the denormalized summaries on each of them. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            fixture.TaskRunner.ClearPending();

            var post = new Post("original title", "content");
            await dbContext.Posts.CreateAsync(post);

            var blog = new Blog("my blog");
            blog.AddPost(post);
            await dbContext.Blogs.CreateAsync(blog);

            //copy the raw blog document into the archived collection, with the same id
            var blogsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("blogs");
            var archivedBlogsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("archivedBlogs");
            var blogFilter = Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(blog.Id));
            var rawBlog = await blogsCollection.Find(blogFilter).SingleAsync();
            await archivedBlogsCollection.InsertOneAsync(rawBlog);

            // Action: update the referenced post, and execute the enqueued task.
            var loadedPost = await dbContext.Posts.FindOneAsync(post.Id);
            loadedPost.Title = "updated title";
            await dbContext.SaveChangesAsync();

            await fixture.TaskRunner.ExecutePendingAsync(fixture.ServiceProvider);

            // Assert: the summaries are refreshed on both hosting collections.
            rawBlog = await blogsCollection.Find(blogFilter).SingleAsync();
            Assert.Equal("updated title", rawBlog["LastPost"]["Title"].AsString);

            var rawArchivedBlog = await archivedBlogsCollection.Find(blogFilter).SingleAsync();
            Assert.Equal("updated title", rawArchivedBlog["LastPost"]["Title"].AsString);
        }

        [Fact]
        public async Task DeletedReferencedModelSkipsTheUpdateWithoutFailing()
        {
            /* A model deleted while its update task was pending has nothing to
             * propagate: the update task skips without failing, so the background
             * executor doesn't retry forever a task that can never succeed. The
             * references then follow the origin delete policy: removed by default,
             * by the delete propagation the domain delete enqueued. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            fixture.TaskRunner.ClearPending();

            var post = new Post("original title", "content");
            await dbContext.Posts.CreateAsync(post);
            var blog = new Blog("my blog");
            blog.AddPost(post);
            await dbContext.Blogs.CreateAsync(blog);

            // Action: update the referenced post, delete it, then execute the enqueued tasks.
            var loadedPost = await dbContext.Posts.FindOneAsync(post.Id);
            loadedPost.Title = "updated title";
            await dbContext.SaveChangesAsync();
            await dbContext.Posts.DeleteAsync(loadedPost);

            await fixture.TaskRunner.ExecutePendingAsync(fixture.ServiceProvider);

            // Assert.
            //the update skipped, and the delete propagation removed the references
            var blogsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("blogs");
            var rawBlog = await blogsCollection.Find(IdFilter(blog.Id)).SingleAsync();
            Assert.Equal(BsonNull.Value, rawBlog["LastPost"]);
            Assert.Empty(rawBlog["Posts"].AsBsonArray);
        }

        [Fact]
        public async Task ExclusiveAccessDeniesPendingDependenciesUpdates()
        {
            /* The task never holds an exclusive access allowance: executed while another
             * flow holds the exclusive access (e.g. a migration), its collection accesses
             * are denied, and the task executor retries later, converging on the post
             * exclusive state instead of interleaving with it. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            fixture.TaskRunner.ClearPending();

            var post = new Post("original title", "content");
            await dbContext.Posts.CreateAsync(post);
            var blog = new Blog("my blog");
            blog.AddPost(post);
            await dbContext.Blogs.CreateAsync(blog);

            var loadedPost = await dbContext.Posts.FindOneAsync(post.Id);
            loadedPost.Title = "updated title";
            await dbContext.SaveChangesAsync();

            // Action + assert: the pending update is denied during the exclusive access.
            await dbContext.Engine.RunWithExclusiveAccessAsync(async () =>
            {
                //the task executes on its own flow, without the exclusive access handler
                using var taskFlowHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
                await Assert.ThrowsAsync<UnauthorizedAccessException>(
                    () => fixture.TaskRunner.ExecutePendingAsync(fixture.ServiceProvider));
            });

            //the denied task wrote nothing
            var blogsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("blogs");
            var rawBlog = await blogsCollection.Find(IdFilter(blog.Id)).SingleAsync();
            Assert.Equal("original title", rawBlog["LastPost"]["Title"].AsString);

            // Action + assert: re-executed after the exclusive access, like the task
            // executor retry would do, the update converges.
            loadedPost.Title = "final title";
            await dbContext.SaveChangesAsync();
            await fixture.TaskRunner.ExecutePendingAsync(fixture.ServiceProvider);

            rawBlog = await blogsCollection.Find(IdFilter(blog.Id)).SingleAsync();
            Assert.Equal("final title", rawBlog["LastPost"]["Title"].AsString);
        }

        [Fact]
        public async Task MismatchedReferencedDbContextTypesAreSkipped()
        {
            /* The referenced repository is identified by db context type and repository
             * name together, since repository names are unique per db context only: a
             * payload carrying the type of another db context matches nothing and skips,
             * without touching the documents. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            fixture.TaskRunner.ClearPending();

            var post = new Post("original title", "content");
            await dbContext.Posts.CreateAsync(post);
            var blog = new Blog("my blog");
            blog.AddPost(post);
            await dbContext.Blogs.CreateAsync(blog);

            var loadedPost = await dbContext.Posts.FindOneAsync(post.Id);
            loadedPost.Title = "updated title";
            await dbContext.SaveChangesAsync();
            fixture.TaskRunner.ClearPending();

            var idMemberMapIds = dbContext.Engine.MapRegistry.MemberMapsById.Values
                .Where(memberMap => memberMap is { IsEntityReferenceMember: true, IsIdMember: true })
                .Select(memberMap => memberMap.Id)
                .ToArray();

            // Action: run the task directly, with the type of another db context.
            using var taskScope = fixture.ServiceProvider.CreateScope();
            var task = taskScope.ServiceProvider.GetRequiredService<IUpdateDocDependenciesTask>();
            await task.RunAsync<TestDbContext>(typeof(SecondDbContext), "posts", post.Id, idMemberMapIds);

            // Assert: the summary is untouched.
            var blogsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("blogs");
            var rawBlog = await blogsCollection.Find(IdFilter(blog.Id)).SingleAsync();
            Assert.Equal("original title", rawBlog["LastPost"]["Title"].AsString);

            // Action and assert: the same payload with the right type refreshes the summary.
            await task.RunAsync<TestDbContext>(typeof(TestDbContext), "posts", post.Id, idMemberMapIds);

            rawBlog = await blogsCollection.Find(IdFilter(blog.Id)).SingleAsync();
            Assert.Equal("updated title", rawBlog["LastPost"]["Title"].AsString);
        }

        [Fact]
        public async Task OutdatedSummariesMigrateToTheActiveReferenceSchema()
        {
            /* The refresh filter matches summaries by their id element path, whatever
             * schema shaped them: a summary persisted with an outdated schema is
             * rewritten with the active reference schema at the first refresh. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            fixture.TaskRunner.ClearPending();

            var author = new Web2Account("alice");
            await dbContext.Accounts.CreateAsync(author);
            var editor = new Web2Account("bob");
            await dbContext.Accounts.CreateAsync(editor);
            var message = new Message("hello", author, editor);
            await dbContext.Messages.CreateAsync(message);

            //degrade the persisted summary to an outdated shape, with an unknown schema id
            var messagesCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("messages");
            await messagesCollection.UpdateOneAsync(IdFilter(message.Id), Builders<BsonDocument>.Update.Set(
                "Author",
                new BsonDocument
                {
                    { "_s", "00000000-0000-0000-0000-000000000000" },
                    { "_t", "Web2Account" },
                    { "_id", ObjectId.Parse(author.Id) }
                }));

            // Action: replace the referenced account, and execute the enqueued task.
            using var replaceScope = fixture.ServiceProvider.CreateScope();
            var replaceDbContext = replaceScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var replaceContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            var loadedAuthor = await replaceDbContext.Accounts.FindOneAsync(author.Id);
            await replaceDbContext.Accounts.ReplaceAsync(loadedAuthor);
            await fixture.TaskRunner.ExecutePendingAsync(fixture.ServiceProvider);

            // Assert.
            var rawAuthor = (await messagesCollection.Find(IdFilter(message.Id)).SingleAsync())["Author"].AsBsonDocument;
            Assert.Equal("f5825985-4d3a-43e0-a15a-e6f504c34e07", rawAuthor["_s"].AsString); //active summary schema
            Assert.Equal("Web2Account", rawAuthor["_t"].AsString);
            Assert.Equal("alice", rawAuthor["Username"].AsString);
        }

        [Fact]
        public async Task ReadOnlyRepositoriesAreSkippedWithoutFailing()
        {
            /* MODM-205: a read-only repository can host referencing documents, owned by
             * another application: the update fan-out skips it — its summaries are not
             * this task's to refresh, and every write would be denied, failing the whole
             * task — while the summaries on the writable repositories refresh normally. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var mixedDbContext = serviceScope.ServiceProvider.GetRequiredService<IMixedAccessDbContext>();
            var secondDbContext = serviceScope.ServiceProvider.GetRequiredService<ISecondDbContext>();

            var track = new Track("original title");
            await mixedDbContext.Tracks.CreateAsync(track);
            var mixtape = new Mixtape("my mixtape") { Highlight = track };
            await mixedDbContext.Mixtapes.CreateAsync(mixtape);

            //copy the raw mixtape into the archived collection, consumed read-only
            var mixtapesCollection = secondDbContext.Engine.Database.GetCollection<BsonDocument>("mixedMixtapes");
            var archivedMixtapesCollection = secondDbContext.Engine.Database.GetCollection<BsonDocument>("archivedMixtapes");
            var rawMixtape = await mixtapesCollection.Find(IdFilter(mixtape.Id)).SingleAsync();
            await archivedMixtapesCollection.InsertOneAsync(rawMixtape);

            // Action: update the referenced track, and execute the enqueued task.
            fixture.TaskRunner.ClearPending();
            var loadedTrack = await mixedDbContext.Tracks.FindOneAsync(track.Id);
            loadedTrack.Title = "updated title";
            await mixedDbContext.SaveChangesAsync();
            await fixture.TaskRunner.ExecutePendingAsync(fixture.ServiceProvider);

            // Assert: the writable summary refreshes, the read-only hosted one is untouched.
            rawMixtape = await mixtapesCollection.Find(IdFilter(mixtape.Id)).SingleAsync();
            Assert.Equal("updated title", rawMixtape["Highlight"]["Title"].AsString);

            var rawArchivedMixtape = await archivedMixtapesCollection.Find(IdFilter(mixtape.Id)).SingleAsync();
            Assert.Equal("original title", rawArchivedMixtape["Highlight"]["Title"].AsString);
        }

        [Fact]
        public async Task RefreshesSummariesInBulkWithoutPerDocumentRoundTrips()
        {
            /* Summaries refresh with a bulk update operation per reference id path: the
             * count of issued update commands doesn't depend on the count of referencing
             * documents, and no per document findAndModify round trip is issued. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            fixture.TaskRunner.ClearPending();

            var post = new Post("original title", "content");
            await dbContext.Posts.CreateAsync(post);

            List<Blog> blogs = [];
            for (int i = 0; i < 10; i++)
            {
                var blog = new Blog($"blog {i}");
                blog.AddPost(post);
                await dbContext.Blogs.CreateAsync(blog);
                blogs.Add(blog);
            }

            //enqueue only the propagation of the post change
            fixture.TaskRunner.ClearPending();
            var loadedPost = await dbContext.Posts.FindOneAsync(post.Id);
            loadedPost.Title = "updated title";
            await dbContext.SaveChangesAsync();

            // Action: execute the enqueued task, counting the issued server commands.
            var commandsBefore = await GetServerCommandCountersAsync();
            await fixture.TaskRunner.ExecutePendingAsync(fixture.ServiceProvider);
            var commandsAfter = await GetServerCommandCountersAsync();

            // Assert.
            //every summary is refreshed
            var blogsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("blogs");
            foreach (var blog in blogs)
            {
                var rawBlog = await blogsCollection.Find(IdFilter(blog.Id)).SingleAsync();
                Assert.Equal("updated title", rawBlog["LastPost"]["Title"].AsString);
            }

            //bulk update commands, not scaling with the referencing documents count
            Assert.Equal(0, commandsAfter.FindAndModify - commandsBefore.FindAndModify);
            Assert.InRange(commandsAfter.Update - commandsBefore.Update, 1, blogs.Count - 1);
        }

        [Fact]
        public async Task SummariesUnderUnknownDocumentKeysStayStale()
        {
            /* MODM-205: a dictionary in document representation writes its keys as element
             * names, unknown to the maps: the task can't address the path server side
             * (querying unknown document keys is unsupported, see upstream SERVER-267), so
             * it skips the path — without failing the task — and the summaries hosted
             * under it stay stale, while the summaries at every addressable path of the
             * same document refresh. The configuration is reported by a warning at engine
             * build. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            fixture.TaskRunner.ClearPending();

            var track = new Track("original title");
            await dbContext.Tracks.CreateAsync(track);

            var mixtape = new Mixtape("my mixtape")
            {
                Highlight = track,
                LabeledTracks = { ["labeled"] = track },
                Tracks = [track]
            };
            await dbContext.Mixtapes.CreateAsync(mixtape);

            // Action: update the referenced track, and execute the enqueued task.
            var loadedTrack = await dbContext.Tracks.FindOneAsync(track.Id);
            loadedTrack.Title = "updated title";
            await dbContext.SaveChangesAsync();
            await fixture.TaskRunner.ExecutePendingAsync(fixture.ServiceProvider);

            // Assert: the addressable paths refresh, the dictionary hosted summary doesn't.
            var mixtapesCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("mixtapes");
            var rawMixtape = await mixtapesCollection.Find(IdFilter(mixtape.Id)).SingleAsync();
            Assert.Equal("updated title", rawMixtape["Highlight"]["Title"].AsString);
            Assert.Equal("updated title", rawMixtape["Tracks"].AsBsonArray[0]["Title"].AsString);
            Assert.Equal("original title", rawMixtape["LabeledTracks"]["labeled"]["Title"].AsString);
        }

        [Fact]
        public async Task UnknownMemberMapIdentifiersAreSkipped()
        {
            /* A scheduled task can execute against a configuration different from the
             * one that enqueued it (e.g. after a software upgrade): identifiers of
             * member maps that don't exist anymore are skipped, without failing the
             * task nor touching the documents. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("title", "content");
            await dbContext.Posts.CreateAsync(post);
            var blog = new Blog("my blog");
            blog.AddPost(post);
            await dbContext.Blogs.CreateAsync(blog);

            var blogsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("blogs");
            var rawBlogBefore = await blogsCollection.Find(IdFilter(blog.Id)).SingleAsync();

            // Action: run the task directly with an unknown member map identifier.
            using var taskScope = fixture.ServiceProvider.CreateScope();
            var task = taskScope.ServiceProvider.GetRequiredService<IUpdateDocDependenciesTask>();
            await task.RunAsync<TestDbContext>(typeof(TestDbContext), "posts", post.Id, ["unknown-member-map-id"]);

            // Assert.
            var rawBlogAfter = await blogsCollection.Find(IdFilter(blog.Id)).SingleAsync();
            Assert.Equal(rawBlogBefore, rawBlogAfter);
        }

        [Fact]
        public async Task UnknownRepositoryNamesAreSkipped()
        {
            /* MODM-205: like the member map identifiers, the repository name of a
             * scheduled task can address a configuration that doesn't exist anymore
             * (e.g. after a software upgrade): the task skips without failing, without
             * touching the documents. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("title", "content");
            await dbContext.Posts.CreateAsync(post);
            var blog = new Blog("my blog");
            blog.AddPost(post);
            await dbContext.Blogs.CreateAsync(blog);

            var blogsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("blogs");
            var rawBlogBefore = await blogsCollection.Find(IdFilter(blog.Id)).SingleAsync();

            // Action: run the task directly with an unknown repository name.
            using var taskScope = fixture.ServiceProvider.CreateScope();
            var task = taskScope.ServiceProvider.GetRequiredService<IUpdateDocDependenciesTask>();
            await task.RunAsync<TestDbContext>(typeof(TestDbContext), "unknownRepository", post.Id, ["unknown-member-map-id"]);

            // Assert.
            var rawBlogAfter = await blogsCollection.Find(IdFilter(blog.Id)).SingleAsync();
            Assert.Equal(rawBlogBefore, rawBlogAfter);
        }

        [Fact]
        public async Task UpdatesEveryReferencingDocumentAndOnlyThose()
        {
            /* The task fans out to every document embedding a summary of the changed
             * model, and only to those: documents referencing other models of the same
             * repository stay untouched. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            fixture.TaskRunner.ClearPending();

            var changedPost = new Post("original title", "content");
            await dbContext.Posts.CreateAsync(changedPost);
            var otherPost = new Post("other title", "content");
            await dbContext.Posts.CreateAsync(otherPost);

            var firstBlog = new Blog("first blog");
            firstBlog.AddPost(changedPost);
            await dbContext.Blogs.CreateAsync(firstBlog);
            var secondBlog = new Blog("second blog");
            secondBlog.AddPost(changedPost);
            await dbContext.Blogs.CreateAsync(secondBlog);
            var untouchedBlog = new Blog("untouched blog");
            untouchedBlog.AddPost(otherPost);
            await dbContext.Blogs.CreateAsync(untouchedBlog);

            // Action.
            var loadedPost = await dbContext.Posts.FindOneAsync(changedPost.Id);
            loadedPost.Title = "updated title";
            await dbContext.SaveChangesAsync();
            await fixture.TaskRunner.ExecutePendingAsync(fixture.ServiceProvider);

            // Assert.
            var blogsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("blogs");
            var rawFirstBlog = await blogsCollection.Find(IdFilter(firstBlog.Id)).SingleAsync();
            var rawSecondBlog = await blogsCollection.Find(IdFilter(secondBlog.Id)).SingleAsync();
            var rawUntouchedBlog = await blogsCollection.Find(IdFilter(untouchedBlog.Id)).SingleAsync();
            Assert.Equal("updated title", rawFirstBlog["LastPost"]["Title"].AsString);
            Assert.Equal("updated title", rawSecondBlog["LastPost"]["Title"].AsString);
            Assert.Equal("other title", rawUntouchedBlog["LastPost"]["Title"].AsString);
        }

        [Fact]
        public async Task UpdatesOnlyTheMatchingArrayItems()
        {
            /* Summaries hosted by collection members update through a filtered array
             * item ($[idfilter]): only the items of the changed model are rewritten,
             * the other items stay untouched. The type change of the referenced account
             * makes the rewrite observable on the item discriminator. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            fixture.TaskRunner.ClearPending();

            var alice = new Web2Account("alice");
            await dbContext.Accounts.CreateAsync(alice);
            var bob = new Web2Account("bob");
            await dbContext.Accounts.CreateAsync(bob);

            var message = new Message("hello", alice, bob);
            message.AddWatcher(alice);
            message.AddWatcher(bob);
            await dbContext.Messages.CreateAsync(message);

            // Action: evolve alice into a web3 account, and execute the enqueued task.
            var web3Alice = new Web3Account(alice, "0x0123456789");
            await dbContext.Accounts.ReplaceAsync(web3Alice);
            await fixture.TaskRunner.ExecutePendingAsync(fixture.ServiceProvider);

            // Assert.
            var messagesCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("messages");
            var rawWatchers = (await messagesCollection.Find(IdFilter(message.Id)).SingleAsync())["Watchers"].AsBsonArray;

            var rawAlice = rawWatchers.Single(w => w["_id"] == ObjectId.Parse(alice.Id)).AsBsonDocument;
            Assert.Equal("Web3Account", rawAlice["_t"].AsString);
            Assert.Equal("06d4e4c1-1e57-4bd0-a071-90fe7d3dbc2a", rawAlice["_s"].AsString); //summary schema of the new type
            Assert.Equal("alice", rawAlice["Username"].AsString);

            var rawBob = rawWatchers.Single(w => w["_id"] == ObjectId.Parse(bob.Id)).AsBsonDocument;
            Assert.Equal("Web2Account", rawBob["_t"].AsString);
            Assert.Equal("f5825985-4d3a-43e0-a15a-e6f504c34e07", rawBob["_s"].AsString); //untouched original summary
            Assert.Equal("bob", rawBob["Username"].AsString);
        }

        [Fact]
        public async Task UpdatesSummariesHostedByArrayOfArraysDictionaries()
        {
            /* MODM-205: the array of arrays representation writes the dictionary entries
             * as [key, value] arrays, hosting the summary at the fixed value position: the
             * reference id element path stays addressable through the fixed index, and the
             * entry values refresh, only on the entries of the changed model. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            fixture.TaskRunner.ClearPending();

            var changedTrack = new Track("original title");
            await dbContext.Tracks.CreateAsync(changedTrack);
            var otherTrack = new Track("other title");
            await dbContext.Tracks.CreateAsync(otherTrack);

            var mixtape = new Mixtape("my mixtape")
            {
                RankedTracks =
                {
                    ["changed"] = changedTrack,
                    ["other"] = otherTrack
                }
            };
            await dbContext.Mixtapes.CreateAsync(mixtape);

            // Action: update the referenced track, and execute the enqueued task.
            var loadedTrack = await dbContext.Tracks.FindOneAsync(changedTrack.Id);
            loadedTrack.Title = "updated title";
            await dbContext.SaveChangesAsync();
            await fixture.TaskRunner.ExecutePendingAsync(fixture.ServiceProvider);

            // Assert.
            var mixtapesCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("mixtapes");
            var rawEntries = (await mixtapesCollection.Find(IdFilter(mixtape.Id)).SingleAsync())["RankedTracks"].AsBsonArray;
            Assert.Equal("updated title", rawEntries.Single(e => e[0] == "changed")[1]["Title"].AsString);
            Assert.Equal("other title", rawEntries.Single(e => e[0] == "other")[1]["Title"].AsString);
        }

        [Fact]
        public async Task UpdatesSummariesHostedByArrayOfDocumentsDictionaries()
        {
            /* MODM-205: the array of documents representation writes the dictionary
             * entries as documents with fixed "k"/"v" element names: the reference id
             * element path stays addressable, and the summaries hosted by the entry
             * values refresh like on any other collection member, only on the entries
             * of the changed model. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            fixture.TaskRunner.ClearPending();

            var changedTrack = new Track("original title");
            await dbContext.Tracks.CreateAsync(changedTrack);
            var otherTrack = new Track("other title");
            await dbContext.Tracks.CreateAsync(otherTrack);

            var mixtape = new Mixtape("my mixtape")
            {
                IndexedTracks =
                {
                    ["changed"] = changedTrack,
                    ["other"] = otherTrack
                }
            };
            await dbContext.Mixtapes.CreateAsync(mixtape);

            // Action: update the referenced track, and execute the enqueued task.
            var loadedTrack = await dbContext.Tracks.FindOneAsync(changedTrack.Id);
            loadedTrack.Title = "updated title";
            await dbContext.SaveChangesAsync();
            await fixture.TaskRunner.ExecutePendingAsync(fixture.ServiceProvider);

            // Assert.
            var mixtapesCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("mixtapes");
            var rawEntries = (await mixtapesCollection.Find(IdFilter(mixtape.Id)).SingleAsync())["IndexedTracks"].AsBsonArray;
            Assert.Equal("updated title", rawEntries.Single(e => e["k"] == "changed")["v"]["Title"].AsString);
            Assert.Equal("other title", rawEntries.Single(e => e["k"] == "other")["v"]["Title"].AsString);
        }

        [Fact]
        public async Task UpdatesSummariesHostedByArraysOfEmbeddedDocuments()
        {
            /* Summaries hosted by a collection member of an embedded document update
             * through the nested element path (Envelope.Recipients.$[idfilter]), with
             * the array filter addressing the id relatively to the array member, not
             * from the document root. The type change of the referenced account makes
             * the rewrite observable on the item discriminator. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            fixture.TaskRunner.ClearPending();

            var alice = new Web2Account("alice");
            await dbContext.Accounts.CreateAsync(alice);
            var bob = new Web2Account("bob");
            await dbContext.Accounts.CreateAsync(bob);

            var message = new Message("hello", alice, bob)
            {
                Envelope = new Envelope([alice, bob])
            };
            await dbContext.Messages.CreateAsync(message);

            // Action: evolve alice into a web3 account, and execute the enqueued task.
            var web3Alice = new Web3Account(alice, "0x0123456789");
            await dbContext.Accounts.ReplaceAsync(web3Alice);
            await fixture.TaskRunner.ExecutePendingAsync(fixture.ServiceProvider);

            // Assert.
            var messagesCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("messages");
            var rawRecipients = (await messagesCollection.Find(IdFilter(message.Id)).SingleAsync())["Envelope"]["Recipients"].AsBsonArray;

            var rawAlice = rawRecipients.Single(r => r["_id"] == ObjectId.Parse(alice.Id)).AsBsonDocument;
            Assert.Equal("Web3Account", rawAlice["_t"].AsString);
            Assert.Equal("06d4e4c1-1e57-4bd0-a071-90fe7d3dbc2a", rawAlice["_s"].AsString); //summary schema of the new type
            Assert.Equal("alice", rawAlice["Username"].AsString);

            var rawBob = rawRecipients.Single(r => r["_id"] == ObjectId.Parse(bob.Id)).AsBsonDocument;
            Assert.Equal("Web2Account", rawBob["_t"].AsString);
            Assert.Equal("f5825985-4d3a-43e0-a15a-e6f504c34e07", rawBob["_s"].AsString); //untouched original summary
            Assert.Equal("bob", rawBob["Username"].AsString);
        }

        [Fact]
        public async Task UpdatesSummariesHostedByNestedArraysOfEmbeddedDocuments()
        {
            /* MODM-205: two array levels with undefined index in the path — an array of
             * embedded documents, each hosting a collection of references — render the
             * outer level as all positions ($[]) and the inner one as the filtered item
             * ($[idfilter]): the summaries of the changed model refresh in every outer
             * item, the other summaries stay untouched. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            fixture.TaskRunner.ClearPending();

            var alice = new Web2Account("alice");
            await dbContext.Accounts.CreateAsync(alice);
            var bob = new Web2Account("bob");
            await dbContext.Accounts.CreateAsync(bob);

            var message = new Message("hello", alice, bob)
            {
                Batches =
                [
                    new Envelope([alice, bob]),
                    new Envelope([alice])
                ]
            };
            await dbContext.Messages.CreateAsync(message);

            // Action: evolve alice into a web3 account, and execute the enqueued task.
            var web3Alice = new Web3Account(alice, "0x0123456789");
            await dbContext.Accounts.ReplaceAsync(web3Alice);
            await fixture.TaskRunner.ExecutePendingAsync(fixture.ServiceProvider);

            // Assert: alice refreshes in every batch, bob stays untouched.
            var messagesCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("messages");
            var rawBatches = (await messagesCollection.Find(IdFilter(message.Id)).SingleAsync())["Batches"].AsBsonArray;

            var rawFirstBatchAlice = rawBatches[0]["Recipients"].AsBsonArray
                .Single(r => r["_id"] == ObjectId.Parse(alice.Id)).AsBsonDocument;
            Assert.Equal("Web3Account", rawFirstBatchAlice["_t"].AsString);
            Assert.Equal("alice", rawFirstBatchAlice["Username"].AsString);

            var rawFirstBatchBob = rawBatches[0]["Recipients"].AsBsonArray
                .Single(r => r["_id"] == ObjectId.Parse(bob.Id)).AsBsonDocument;
            Assert.Equal("Web2Account", rawFirstBatchBob["_t"].AsString);

            var rawSecondBatchAlice = rawBatches[1]["Recipients"].AsBsonArray
                .Single(r => r["_id"] == ObjectId.Parse(alice.Id)).AsBsonDocument;
            Assert.Equal("Web3Account", rawSecondBatchAlice["_t"].AsString);
        }

        [Fact]
        public async Task UpdatesSummariesHostedUnderThreeEmbeddingLevels()
        {
            /* MODM-248: a reference denormalized under three embedding levels (the
             * dispatch, its envelope, the recipients collection) has its member maps at
             * the fourth level from the document root: they register like the shallower
             * ones, so the change of the referenced account refreshes the deep summary. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            fixture.TaskRunner.ClearPending();

            var alice = new Web2Account("alice");
            await dbContext.Accounts.CreateAsync(alice);

            var message = new Message("hello", alice, alice)
            {
                Dispatch = new Dispatch(new Envelope([alice]))
            };
            await dbContext.Messages.CreateAsync(message);

            // Action: evolve alice into a web3 account, and execute the enqueued task.
            var web3Alice = new Web3Account(alice, "0x0123456789");
            await dbContext.Accounts.ReplaceAsync(web3Alice);
            await fixture.TaskRunner.ExecutePendingAsync(fixture.ServiceProvider);

            // Assert.
            var messagesCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("messages");
            var rawMessage = await messagesCollection.Find(IdFilter(message.Id)).SingleAsync();

            var rawAlice = rawMessage["Dispatch"]["Envelope"]["Recipients"].AsBsonArray.Single().AsBsonDocument;
            Assert.Equal("Web3Account", rawAlice["_t"].AsString);
            Assert.Equal("06d4e4c1-1e57-4bd0-a071-90fe7d3dbc2a", rawAlice["_s"].AsString); //summary schema of the new type
            Assert.Equal("alice", rawAlice["Username"].AsString);
        }

        // Helpers.
        private async Task<(long FindAndModify, long Update)> GetServerCommandCountersAsync()
        {
            var serverStatus = await dbContext.Engine.Database.RunCommandAsync<BsonDocument>(
                new BsonDocument("serverStatus", 1));
            var commandCounters = serverStatus["metrics"]["commands"].AsBsonDocument;
            return (commandCounters["findAndModify"]["total"].ToInt64(),
                    commandCounters["update"]["total"].ToInt64());
        }

        private static FilterDefinition<BsonDocument> IdFilter(string id) =>
            Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(id));
    }
}
