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
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.IntegrationTests.Fixtures;
using Etherna.MongODM.IntegrationTests.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.MongODM.IntegrationTests
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
            /* MODM-117: the scan reads the distinct referenced ids of every reference element
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
        public async Task RemovesTheReferencesToMissingOriginDocuments()
        {
            /* The removal scans like the find does and repairs what it verifies: a reference
             * hosted as an array item is pulled out of its array, a single valued one is set
             * to null. Valid references stay untouched, and the repaired document loads
             * normally, reading the removed references as null. */

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
            var report = await dbContext.Blogs.RemoveMissingOriginReferencesAsync();

            // Assert: the removal reports both repaired paths.
            Assert.Empty(report.UnverifiableElementPaths);
            var lastPostRemoval = report.PathRemovals.Single(pathRemoval => pathRemoval.ElementPath == "LastPost");
            Assert.True(lastPostRemoval.MissingOriginIdsCount >= 1);
            Assert.True(lastPostRemoval.UpdatedDocumentsCount >= 1);
            var postsRemoval = report.PathRemovals.Single(pathRemoval => pathRemoval.ElementPath == "Posts");
            Assert.True(postsRemoval.MissingOriginIdsCount >= 1);
            Assert.True(postsRemoval.UpdatedDocumentsCount >= 1);

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
        public async Task RemovesTheReferencesInsideDictionaryValues()
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
            var report = await dbContext.Catalogs.RemoveMissingOriginReferencesAsync();

            // Assert.
            var indexedRemoval = Assert.Single(report.PathRemovals);
            Assert.Equal("IndexedPosts", indexedRemoval.ElementPath);
            Assert.Equal(1, indexedRemoval.MissingOriginIdsCount);
            Assert.Equal(1, indexedRemoval.UpdatedDocumentsCount);

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
        public async Task RemovalIsDeniedOnReadOnlyRepositories()
        {
            /* A read-only repository denies every write on its collection: the removal fails
             * fast, before scanning anything. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var readOnlyDbContext = serviceScope.ServiceProvider.GetRequiredService<IReadOnlyDbContext>();

            // Action and assert.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => readOnlyDbContext.Notes.RemoveMissingOriginReferencesAsync());
        }

        // Helpers.
        /* Delete an origin document out of any domain flow, like a raw cleanup or another
         * application would do: the references pointing to it stay on their documents. */
        private async Task DeleteRawPostAsync(string postId)
        {
            var postsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("posts");
            await postsCollection.DeleteOneAsync(Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(postId)));
        }

        private static MissingOriginReferencesPathReport GetPathReport(
            MissingOriginReferencesReport report,
            string elementPath) =>
            report.PathReports.Single(pathReport => pathReport.ElementPath == elementPath);

        /* The catalogs collection is used by these tests only: purging it keeps their
         * exact assertions independent from the execution order. */
        private async Task ResetCatalogsAsync()
        {
            var catalogsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("catalogs");
            await catalogsCollection.DeleteManyAsync(Builders<BsonDocument>.Filter.Empty);
        }
    }
}
