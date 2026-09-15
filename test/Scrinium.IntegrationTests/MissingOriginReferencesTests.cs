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
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Domain.Models.ReferencesRepairOpAgg;
using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Etherna.Scrinium.Core.Options;
using Etherna.Scrinium.Core.Repositories;
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
    public class MissingOriginReferencesTests : IDisposable
    {
        // Fields.
        private readonly ITestDbContext dbContext;
        private readonly IntegrationFixture fixture;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public MissingOriginReferencesTests(IntegrationFixture fixture)
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
        public async Task FindReportsTheReferencesToMissingOriginDocuments()
        {
            /* SCR-117: the scan reads the distinct referenced ids of every reference element
             * path and verifies them against the origin collection: a deleted origin document
             * reports on every path referencing it, while valid and null references stay out. */

            // Setup.
            /* The collections are shared with the other integration tests: assert on deltas
             * from a baseline, and on the ids of the documents created here. */
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var baseline = await dbContext.Blogs.FindMissingOriginReferencesAsync();

            var brokenPost = new Post("broken", "content");
            var validPost = new Post("valid", "content");
            var blog = new Blog("blog title");
            blog.AddPost(validPost);
            blog.AddPost(brokenPost); //also the last post
            await dbContext.Blogs.CreateAsync(blog);
            var nullReferenceBlog = new Blog("no posts"); //null last post, empty posts array
            await dbContext.Blogs.CreateAsync(nullReferenceBlog);

            await DeleteRawPostAsync(brokenPost.Id);

            // Action.
            var report = await dbContext.Blogs.FindMissingOriginReferencesAsync();

            // Assert.
            Assert.Empty(report.UnverifiableElementPaths);

            //the single reference path
            var lastPostReport = report.PathReports.Single(pathReport => pathReport.ElementPath == "LastPost");
            Assert.Equal(["posts"], lastPostReport.OriginRepositoryNames);
            Assert.Equal(
                GetPathReport(baseline, "LastPost").MissingOriginIdsCount + 1,
                lastPostReport.MissingOriginIdsCount);
            Assert.Contains(brokenPost.Id, lastPostReport.TrackedMissingOriginIds);
            Assert.DoesNotContain(validPost.Id, lastPostReport.TrackedMissingOriginIds);
            Assert.True(lastPostReport.ReferencingDocumentsCount >= 1);

            //the array items reference path
            var postsReport = report.PathReports.Single(pathReport => pathReport.ElementPath == "Posts");
            Assert.Equal(["posts"], postsReport.OriginRepositoryNames);
            Assert.Equal(
                GetPathReport(baseline, "Posts").MissingOriginIdsCount + 1,
                postsReport.MissingOriginIdsCount);
            Assert.Contains(brokenPost.Id, postsReport.TrackedMissingOriginIds);
            Assert.DoesNotContain(validPost.Id, postsReport.TrackedMissingOriginIds);
        }

        [Fact]
        public async Task FindVerifiesTheNestedReferencePaths()
        {
            /* A summary can denormalize another reference among its members: the nested
             * reference verifies at its composed element path, against its own origin
             * collection. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var baseline = await dbContext.Bookmarks.FindMissingOriginReferencesAsync();

            var post = new Post("post title", "content");
            var blog = new Blog("blog title");
            blog.AddPost(post);
            await dbContext.Blogs.CreateAsync(blog);
            var bookmark = new Bookmark("my bookmark", blog);
            await dbContext.Bookmarks.CreateAsync(bookmark);

            await DeleteRawPostAsync(post.Id);

            // Action.
            var report = await dbContext.Bookmarks.FindMissingOriginReferencesAsync();

            // Assert: the blog origin document exists, its nested post reference is broken.
            var blogReport = report.PathReports.Single(pathReport => pathReport.ElementPath == "Blog");
            Assert.Equal(["blogs"], blogReport.OriginRepositoryNames);
            Assert.DoesNotContain(blog.Id, blogReport.TrackedMissingOriginIds);

            var nestedPostReport = report.PathReports.Single(pathReport => pathReport.ElementPath == "Blog.LastPost");
            Assert.Equal(["posts"], nestedPostReport.OriginRepositoryNames);
            Assert.Equal(
                GetPathReport(baseline, "Blog.LastPost").MissingOriginIdsCount + 1,
                nestedPostReport.MissingOriginIdsCount);
            Assert.Contains(post.Id, nestedPostReport.TrackedMissingOriginIds);
        }

        [Fact]
        public async Task FindReportsTheUnverifiableReferencePaths()
        {
            /* A dictionary in document representation writes its keys as element names,
             * unknown to the maps: the scan can't address its referenced ids server side, so
             * the path reports as unverifiable and its references stay out of the reports.
             * The array of documents representation keeps the ids addressable instead. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await ResetCatalogsAsync();

            var brokenPost = new Post("broken", "content");
            var validPost = new Post("valid", "content");
            var catalog = new Catalog
            {
                IndexedPosts = new Dictionary<string, Post>
                {
                    ["broken"] = brokenPost,
                    ["valid"] = validPost
                },
                LabeledPosts = new Dictionary<string, Post>
                {
                    ["labeled"] = brokenPost
                }
            };
            await dbContext.Catalogs.CreateAsync(catalog);

            await DeleteRawPostAsync(brokenPost.Id);

            // Action.
            var report = await dbContext.Catalogs.FindMissingOriginReferencesAsync();

            // Assert.
            Assert.Equal(["LabeledPosts"], report.UnverifiableElementPaths);

            var indexedReport = Assert.Single(report.PathReports);
            Assert.Equal("IndexedPosts", indexedReport.ElementPath);
            Assert.Equal(["posts"], indexedReport.OriginRepositoryNames);
            Assert.Equal(1, indexedReport.MissingOriginIdsCount);
            Assert.Equal([brokenPost.Id], indexedReport.TrackedMissingOriginIds);
            Assert.Equal(1, indexedReport.ReferencingDocumentsCount);
        }

        [Fact]
        public async Task FindCapsTheTrackedMissingOriginIdsListing()
        {
            /* The scan keeps counting the missing origin ids beyond the tracking cap: the
             * listing stays bounded, the counts report the full amounts, and the referencing
             * documents count over the tracked ids stays a lower bound. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await ResetCatalogsAsync();

            var overCapCount = MissingOriginReferencesPathReport.MaxTrackedMissingOriginIds + 20;
            var posts = Enumerable.Range(0, overCapCount)
                .Select(i => new Post($"post {i}", "content"))
                .ToArray();
            var catalog = new Catalog
            {
                IndexedPosts = posts.ToDictionary(post => post.Title, post => post)
            };
            await dbContext.Catalogs.CreateAsync(catalog);

            var postsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("posts");
            await postsCollection.DeleteManyAsync(Builders<BsonDocument>.Filter.In(
                "_id",
                posts.Select(post => ObjectId.Parse(post.Id))));

            // Action.
            var report = await dbContext.Catalogs.FindMissingOriginReferencesAsync();

            // Assert.
            var indexedReport = Assert.Single(report.PathReports);
            Assert.Equal(overCapCount, indexedReport.MissingOriginIdsCount);
            Assert.Equal(
                MissingOriginReferencesPathReport.MaxTrackedMissingOriginIds,
                indexedReport.TrackedMissingOriginIds.Count);
            Assert.Equal(1, indexedReport.ReferencingDocumentsCount);
        }

        [Fact]
        public async Task RepairRemovesTheReferencesToMissingOriginDocuments()
        {
            /* The repair scans like the find does and repairs what it verifies: on a path
             * whose mapping removes the reference — the default policy — a reference hosted as
             * an array item is pulled out of its array, a single valued one is set to null.
             * Valid references stay untouched, and the repaired document loads normally,
             * reading the removed references as null. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var brokenPost = new Post("broken", "content");
            var validPost = new Post("valid", "content");
            var blog = new Blog("blog title");
            blog.AddPost(validPost);
            blog.AddPost(brokenPost); //also the last post
            await dbContext.Blogs.CreateAsync(blog);

            await DeleteRawPostAsync(brokenPost.Id);

            // Action.
            var report = await dbContext.Blogs.RepairMissingOriginReferencesAsync();

            // Assert: the repair reports both repaired paths.
            Assert.Empty(report.UnverifiableElementPaths);
            var lastPostRepair = report.PathRepairs.Single(pathRepair => pathRepair.ElementPath == "LastPost");
            Assert.Equal(OriginDeleteMode.RemoveReference, lastPostRepair.RepairMode);
            Assert.True(lastPostRepair.MissingOriginIdsCount >= 1);
            Assert.True(lastPostRepair.UpdatedDocumentsCount >= 1);
            var postsRepair = report.PathRepairs.Single(pathRepair => pathRepair.ElementPath == "Posts");
            Assert.True(postsRepair.MissingOriginIdsCount >= 1);
            Assert.True(postsRepair.UpdatedDocumentsCount >= 1);

            //the raw document: the single reference is null, the array kept only the valid item
            var blogsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("blogs");
            var rawBlog = await blogsCollection
                .Find(Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(blog.Id)))
                .SingleAsync();
            Assert.Equal(BsonNull.Value, rawBlog["LastPost"]);
            var rawPostsItem = Assert.Single(rawBlog["Posts"].AsBsonArray);
            Assert.Equal(ObjectId.Parse(validPost.Id), rawPostsItem["_id"].AsObjectId);

            //a following scan doesn't report the removed references anymore
            var followingReport = await dbContext.Blogs.FindMissingOriginReferencesAsync();
            Assert.DoesNotContain(brokenPost.Id,
                GetPathReport(followingReport, "LastPost").TrackedMissingOriginIds);
            Assert.DoesNotContain(brokenPost.Id,
                GetPathReport(followingReport, "Posts").TrackedMissingOriginIds);

            //the repaired document loads normally on a fresh scope
            using var readScope = fixture.ServiceProvider.CreateScope();
            var readDbContext = readScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedBlog = await readDbContext.Blogs.FindOneAsync(blog.Id);
            Assert.Null(loadedBlog.LastPost);
            var loadedPost = Assert.Single(loadedBlog.Posts);
            Assert.Equal(validPost.Id, loadedPost.Id);
        }

        [Fact]
        public async Task RepairRemovesTheReferencesInsideDictionaryValues()
        {
            /* A reference hosted as a dictionary value in array of documents representation
             * is not an array item itself: the removal sets it to null inside its entry,
             * addressing the entry through an array filter on the missing origin id. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await ResetCatalogsAsync();

            var brokenPost = new Post("broken", "content");
            var validPost = new Post("valid", "content");
            var catalog = new Catalog
            {
                IndexedPosts = new Dictionary<string, Post>
                {
                    ["broken"] = brokenPost,
                    ["valid"] = validPost
                }
            };
            await dbContext.Catalogs.CreateAsync(catalog);

            await DeleteRawPostAsync(brokenPost.Id);

            // Action.
            var report = await dbContext.Catalogs.RepairMissingOriginReferencesAsync();

            // Assert.
            var indexedRepair = Assert.Single(report.PathRepairs);
            Assert.Equal("IndexedPosts", indexedRepair.ElementPath);
            Assert.Equal(OriginDeleteMode.RemoveReference, indexedRepair.RepairMode);
            Assert.Equal(1, indexedRepair.MissingOriginIdsCount);
            Assert.Equal(1, indexedRepair.UpdatedDocumentsCount);

            //the raw document: the broken entry keeps its key with a null value, the valid one is untouched
            var catalogsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("catalogs");
            var rawCatalog = await catalogsCollection
                .Find(Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(catalog.Id)))
                .SingleAsync();
            var rawEntries = rawCatalog["IndexedPosts"].AsBsonArray;
            Assert.Equal(2, rawEntries.Count);
            Assert.Equal(BsonNull.Value, rawEntries.Single(entry => entry["k"] == "broken")["v"]);
            Assert.Equal(
                ObjectId.Parse(validPost.Id),
                rawEntries.Single(entry => entry["k"] == "valid")["v"]["_id"].AsObjectId);
        }

        [Fact]
        public async Task FindVerifiesTheCrossDbContextReferences()
        {
            /* A reference declaring its source on a child db context verifies against the
             * child collection, in its own database: the scan resolves the origin repository
             * like a lazy load would do. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var parentDbContext = serviceScope.ServiceProvider.GetRequiredService<IParentDbContext>();
            var secondDbContext = serviceScope.ServiceProvider.GetRequiredService<ISecondDbContext>();
            var baseline = await parentDbContext.Journals.FindMissingOriginReferencesAsync();

            var note = new Note("note text");
            await secondDbContext.Notes.CreateAsync(note);
            var journal = new Journal("journal title") { PinnedNote = note };
            await parentDbContext.Journals.CreateAsync(journal);

            var notesCollection = secondDbContext.Engine.Database.GetCollection<BsonDocument>("notes");
            await notesCollection.DeleteOneAsync(Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(note.Id)));

            // Action.
            var report = await parentDbContext.Journals.FindMissingOriginReferencesAsync();

            // Assert.
            Assert.Empty(report.UnverifiableElementPaths);
            var pinnedNoteReport = report.PathReports.Single(pathReport => pathReport.ElementPath == "PinnedNote");
            Assert.Equal(["notes"], pinnedNoteReport.OriginRepositoryNames);
            Assert.Equal(
                GetPathReport(baseline, "PinnedNote").MissingOriginIdsCount + 1,
                pinnedNoteReport.MissingOriginIdsCount);
            Assert.Contains(note.Id, pinnedNoteReport.TrackedMissingOriginIds);
        }

        [Fact]
        public async Task RepairIsDeniedOnReadOnlyRepositories()
        {
            /* A read-only repository denies every write on its collection: the repair fails
             * fast, before scanning anything. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var readOnlyDbContext = serviceScope.ServiceProvider.GetRequiredService<IReadOnlyDbContext>();

            // Action and assert.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => readOnlyDbContext.Notes.RepairMissingOriginReferencesAsync());
        }

        [Fact]
        public async Task FindReportsTheOriginDeletePolicyOfEachPath()
        {
            /* The mapping declares what the deletion of an origin document does to the
             * documents referencing it: the scan reports it per path, since it is what a
             * repair of that path follows by default. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            // Action.
            var mixtapesReport = await dbContext.Mixtapes.FindMissingOriginReferencesAsync();
            var royaltiesReport = await dbContext.Royalties.FindMissingOriginReferencesAsync();

            // Assert.
            Assert.Equal(
                OriginDeleteMode.RemoveReference,
                GetPathReport(mixtapesReport, "Highlight").OriginDelete);
            Assert.Equal(
                OriginDeleteMode.KeepReference,
                GetPathReport(mixtapesReport, "Pinned").OriginDelete);
            Assert.Equal(
                OriginDeleteMode.DeleteReferencingDocument,
                GetPathReport(royaltiesReport, "Subject").OriginDelete);
        }

        [Fact]
        public async Task FindListsTheDocumentsCarryingTheDanglingReferences()
        {
            /* The missing origin ids say what is broken, the referencing document ids say
             * where: an operator looks those documents up before repairing anything. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var brokenPost = new Post("broken", "content");
            var blog = new Blog("blog title");
            blog.AddPost(brokenPost); //also the last post
            await dbContext.Blogs.CreateAsync(blog);

            await DeleteRawPostAsync(brokenPost.Id);

            // Action.
            var report = await dbContext.Blogs.FindMissingOriginReferencesAsync();

            // Assert.
            var lastPostReport = GetPathReport(report, "LastPost");
            Assert.Contains(brokenPost.Id, lastPostReport.TrackedMissingOriginIds);
            Assert.Contains(blog.Id, lastPostReport.TrackedReferencingDocumentIds);
            Assert.True(lastPostReport.ReferencingDocumentsCount >= 1);
        }

        [Fact]
        public async Task RepairOperationAppliesTheChosenModesAndReportsEachPath()
        {
            /* The repair runs as a db operation: it carries its plan from the moment it opens,
             * claims the db context lock, and reports every path it was started with. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await ResetRoyaltiesAsync();
            fixture.TaskRunner.ClearPending();

            var track = new Track("track");
            await dbContext.Tracks.CreateAsync(track);
            var royalty = new Royalty("author", track);
            await dbContext.Royalties.CreateAsync(royalty);

            await DeleteRawTrackAsync(track.Id);

            // Action.
            var repairOp = await dbContext.TryStartReferencesRepairAsync(
                "royalties",
                new Dictionary<string, OriginDeleteMode> { ["Subject"] = OriginDeleteMode.DeleteReferencingDocument });
            Assert.NotNull(repairOp);

            //the plan is readable before anything runs
            Assert.Equal(ReferencesRepairOperation.Status.New, repairOp.CurrentStatus);
            var plannedPath = Assert.Single(repairOp.PathStates);
            Assert.Equal("Subject", plannedPath.ElementPath);
            Assert.Equal(OriginDeleteMode.DeleteReferencingDocument, plannedPath.RepairMode);
            Assert.Equal(ReferencesRepairPathState.ExecutionState.Pending, plannedPath.State);

            await dbContext.ExecuteReferencesRepairAsync(repairOp.Id);

            // Assert.
            using var verifyScope = fixture.ServiceProvider.CreateScope();
            var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            var completedOp = await verifyDbContext.GetReferencesRepairAsync(repairOp.Id);

            Assert.Equal(ReferencesRepairOperation.Status.Completed, completedOp.CurrentStatus);
            var repairedPath = Assert.Single(completedOp.PathStates);
            Assert.Equal(ReferencesRepairPathState.ExecutionState.Succeded, repairedPath.State);
            Assert.Equal(1, repairedPath.MissingOriginIdsCount);
            Assert.Equal(1, repairedPath.DeletedDocumentsCount);
            Assert.Equal(0, repairedPath.UpdatedDocumentsCount);

            //the referencing document is gone, and the lock is free for the next operation
            Assert.Null(await dbContext.Royalties.TryFindOneAsync(royalty.Id));
            Assert.False(await dbContext.Engine.DbContextLock.IsLockedAsync());
        }

        [Fact]
        public async Task DryRunRepairOperationReportsWithoutPersisting()
        {
            /* A dry run executes the same operation with its collection writes simulated: it
             * reports what it would repair, and the documents stay as they are. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await ResetRoyaltiesAsync();
            fixture.TaskRunner.ClearPending();

            var track = new Track("track");
            await dbContext.Tracks.CreateAsync(track);
            var royalty = new Royalty("author", track);
            await dbContext.Royalties.CreateAsync(royalty);

            await DeleteRawTrackAsync(track.Id);

            // Action.
            var repairOp = await dbContext.TryStartReferencesRepairAsync(
                "royalties",
                new Dictionary<string, OriginDeleteMode> { ["Subject"] = OriginDeleteMode.DeleteReferencingDocument },
                dryRun: true);
            Assert.NotNull(repairOp);
            await dbContext.ExecuteReferencesRepairAsync(repairOp.Id);

            // Assert.
            using var verifyScope = fixture.ServiceProvider.CreateScope();
            var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            var completedOp = await verifyDbContext.GetReferencesRepairAsync(repairOp.Id);

            Assert.True(completedOp.IsDryRun);
            Assert.Equal(ReferencesRepairOperation.Status.Completed, completedOp.CurrentStatus);
            var simulatedPath = Assert.Single(completedOp.PathStates);
            Assert.Equal(1, simulatedPath.MissingOriginIdsCount);
            Assert.Equal(1, simulatedPath.DeletedDocumentsCount);

            //nothing was persisted: the referencing document is still there
            Assert.NotNull(await dbContext.Royalties.TryFindOneAsync(royalty.Id));
        }

        [Fact]
        public async Task RepairAppliesTheRequestedModeOverTheMapping()
        {
            /* The declared policy is the default, not the only option: an operator repairing a
             * cascade path by removing the reference keeps the referencing documents. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await ResetRoyaltiesAsync();

            var track = new Track("track");
            await dbContext.Tracks.CreateAsync(track);
            var royalty = new Royalty("author", track);
            await dbContext.Royalties.CreateAsync(royalty);

            await DeleteRawTrackAsync(track.Id);

            // Action.
            var report = await dbContext.Royalties.RepairMissingOriginReferencesAsync(
                new Dictionary<string, OriginDeleteMode> { ["Subject"] = OriginDeleteMode.RemoveReference });

            // Assert.
            var subjectRepair = GetPathRepair(report, "Subject");
            Assert.Equal(OriginDeleteMode.RemoveReference, subjectRepair.RepairMode);
            Assert.Equal(0, subjectRepair.DeletedDocumentsCount);
            Assert.True(subjectRepair.UpdatedDocumentsCount >= 1);

            //the referencing document survives, with its dangling reference removed
            var rawRoyalty = await ReadRawRoyaltyAsync(royalty.Id);
            Assert.Equal(BsonNull.Value, rawRoyalty["Subject"]);
        }

        [Fact]
        public async Task RepairDeletesTheReferencingDocumentsWhenTheMappingDeclaresIt()
        {
            /* A reference declaring the referencing document delete on origin delete has its
             * documents deleted by the repair too: the dangling references it finds are the
             * ones that propagation never reached. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await ResetRoyaltiesAsync();
            fixture.TaskRunner.ClearPending();

            var deletedTrack = new Track("deleted");
            var keptTrack = new Track("kept");
            await dbContext.Tracks.CreateAsync(deletedTrack);
            await dbContext.Tracks.CreateAsync(keptTrack);

            var danglingRoyalty = new Royalty("author", deletedTrack);
            var validRoyalty = new Royalty("author", keptTrack);
            await dbContext.Royalties.CreateAsync(danglingRoyalty);
            await dbContext.Royalties.CreateAsync(validRoyalty);

            //the origin document leaves out of any domain flow: nothing propagated the delete
            await DeleteRawTrackAsync(deletedTrack.Id);

            // Action.
            var report = await dbContext.Royalties.RepairMissingOriginReferencesAsync();

            // Assert.
            var subjectRepair = GetPathRepair(report, "Subject");
            Assert.Equal(OriginDeleteMode.DeleteReferencingDocument, subjectRepair.RepairMode);
            Assert.Equal(0, subjectRepair.UpdatedDocumentsCount);
            Assert.Equal(1, subjectRepair.DeletedDocumentsCount);

            //the referencing document is gone, the one on a living origin stays
            Assert.Null(await dbContext.Royalties.TryFindOneAsync(danglingRoyalty.Id));
            Assert.NotNull(await dbContext.Royalties.TryFindOneAsync(validRoyalty.Id));

            //the delete went through the domain: it propagates its own reference policies
            Assert.Contains(fixture.TaskRunner.PendingModelIds, id => Equals(id, danglingRoyalty.Id));
        }

        [Fact]
        public async Task RepairKeepsTheReferencesTheMappingKeeps()
        {
            /* A reference explicitly declaring to keep the reference on origin delete keeps
             * its dangling references by design: the repair leaves that path alone, while the
             * paths of the same collection following the default are repaired. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var track = new Track("track");
            await dbContext.Tracks.CreateAsync(track);

            var mixtape = new Mixtape("mixtape") { Highlight = track, Pinned = track };
            await dbContext.Mixtapes.CreateAsync(mixtape);

            await DeleteRawTrackAsync(track.Id);

            // Action.
            var report = await dbContext.Mixtapes.RepairMissingOriginReferencesAsync();

            // Assert.
            var pinnedRepair = GetPathRepair(report, "Pinned");
            Assert.Equal(OriginDeleteMode.KeepReference, pinnedRepair.RepairMode);
            //a kept path isn't even scanned
            Assert.Equal(0, pinnedRepair.MissingOriginIdsCount);
            Assert.Equal(OriginDeleteMode.RemoveReference, GetPathRepair(report, "Highlight").RepairMode);

            var rawMixtape = await ReadRawMixtapeAsync(mixtape.Id);
            Assert.Equal(ObjectId.Parse(track.Id), rawMixtape["Pinned"]["_id"].AsObjectId);
            Assert.Equal(BsonNull.Value, rawMixtape["Highlight"]);
        }

        [Fact]
        public async Task RepairRefusesAnElementPathTheCollectionDoesntHave()
        {
            /* A mode addressing a path this collection has no verifiable reference at is a
             * caller error, not something to apply silently to nothing. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            // Action and assert.
            await Assert.ThrowsAsync<ArgumentException>(
                () => dbContext.Royalties.RepairMissingOriginReferencesAsync(
                    new Dictionary<string, OriginDeleteMode> { ["NotAReference"] = OriginDeleteMode.RemoveReference }));
        }

        // Helpers.
        /* Delete an origin document out of any domain flow, like a raw cleanup or another
         * application would do: the references pointing to it stay on their documents. */
        private async Task DeleteRawPostAsync(string postId)
        {
            var postsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("posts");
            await postsCollection.DeleteOneAsync(Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(postId)));
        }

        /* Delete an origin track out of any domain flow, like a raw cleanup or another
         * application would do: the references pointing to it stay on their documents. */
        private async Task DeleteRawTrackAsync(string trackId)
        {
            var tracksCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("tracks");
            await tracksCollection.DeleteOneAsync(Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(trackId)));
        }

        private static MissingOriginReferencesPathRepair GetPathRepair(
            MissingOriginReferencesRepairReport report,
            string elementPath) =>
            report.PathRepairs.Single(pathRepair => pathRepair.ElementPath == elementPath);

        private static MissingOriginReferencesPathReport GetPathReport(
            MissingOriginReferencesReport report,
            string elementPath) =>
            report.PathReports.Single(pathReport => pathReport.ElementPath == elementPath);

        private async Task<BsonDocument> ReadRawMixtapeAsync(string mixtapeId) =>
            await dbContext.Engine.Database.GetCollection<BsonDocument>("mixtapes")
                .Find(Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(mixtapeId)))
                .SingleAsync();

        private async Task<BsonDocument> ReadRawRoyaltyAsync(string royaltyId) =>
            await dbContext.Engine.Database.GetCollection<BsonDocument>("royalties")
                .Find(Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(royaltyId)))
                .SingleAsync();

        /* The catalogs collection is used by these tests only: purging it keeps their
         * exact assertions independent from the execution order. */
        private async Task ResetCatalogsAsync()
        {
            var catalogsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("catalogs");
            await catalogsCollection.DeleteManyAsync(Builders<BsonDocument>.Filter.Empty);
        }

        /* A repair of the royalties deletes every royalty whose subject track is missing:
         * purging the collection keeps the exact counts of these tests independent from the
         * documents the other tests left there. */
        private async Task ResetRoyaltiesAsync()
        {
            var royaltiesCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("royalties");
            await royaltiesCollection.DeleteManyAsync(Builders<BsonDocument>.Filter.Empty);
        }
    }
}
