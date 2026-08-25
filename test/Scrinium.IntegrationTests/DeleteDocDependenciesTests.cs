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
using Etherna.MongODM.Core.Tasks;
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
    public class DeleteDocDependenciesTests : IDisposable
    {
        // Fields.
        private readonly ITestDbContext dbContext;
        private readonly IntegrationFixture fixture;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public DeleteDocDependenciesTests(IntegrationFixture fixture)
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
        public async Task DomainDeleteRemovesTheReferencesByDefault()
        {
            /* MODM-19: a reference without a declared origin delete policy gets removed from
             * the referencing documents when the referenced model is deleted through its
             * repository — the default, so a domain delete never leaves dangling references:
             * a reference hosted as an array item is pulled out of its array, a single valued
             * one is set to null. A dictionary in document representation writes its keys as
             * element names, unknown to the maps: its path can't be addressed, and its
             * reference stays, found by the missing origin references scan. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await ResetPropagationCollectionsAsync();
            fixture.TaskRunner.ClearPending();

            var deletedTrack = new Track("deleted");
            var keptTrack = new Track("kept");
            var mixtape = new Mixtape("mixtape")
            {
                Highlight = deletedTrack,
                Tracks = [keptTrack, deletedTrack],
                LabeledTracks = { ["labeled"] = deletedTrack }
            };
            await dbContext.Mixtapes.CreateAsync(mixtape);

            // Action.
            await dbContext.Tracks.DeleteAsync(deletedTrack);
            await ExecutePendingTasksAsync();

            // Assert.
            var rawMixtape = await GetRawDocumentAsync("mixtapes", mixtape.Id);
            Assert.Equal(BsonNull.Value, rawMixtape["Highlight"]);
            var rawTracksItem = Assert.Single(rawMixtape["Tracks"].AsBsonArray);
            Assert.Equal(ObjectId.Parse(keptTrack.Id), rawTracksItem["_id"].AsObjectId);
            //the unaddressable dictionary path keeps its dangling reference
            Assert.Equal(
                ObjectId.Parse(deletedTrack.Id),
                rawMixtape["LabeledTracks"]["labeled"]["_id"].AsObjectId);

            //the repaired document loads normally on a fresh scope
            using var readScope = fixture.ServiceProvider.CreateScope();
            var readDbContext = readScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedMixtape = await readDbContext.Mixtapes.FindOneAsync(mixtape.Id);
            Assert.Null(loadedMixtape.Highlight);
            var loadedTrack = Assert.Single(loadedMixtape.Tracks);
            Assert.Equal(keptTrack.Id, loadedTrack.Id);
        }

        [Fact]
        public async Task DomainDeleteCascadesToTheReferencingDocuments()
        {
            /* A reference declaring the referencing document delete cascades: deleting the
             * track deletes its royalties with a domain delete, whose own references chain
             * the cascade to the royalty audits. Documents referencing other origins stay. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await ResetPropagationCollectionsAsync();
            fixture.TaskRunner.ClearPending();

            var deletedTrack = new Track("deleted");
            var keptTrack = new Track("kept");
            var cascadedRoyalty = new Royalty("author", deletedTrack);
            var keptRoyalty = new Royalty("author", keptTrack);
            await dbContext.Royalties.CreateAsync(cascadedRoyalty);
            await dbContext.Royalties.CreateAsync(keptRoyalty);
            var cascadedAudit = new RoyaltyAudit(cascadedRoyalty);
            var keptAudit = new RoyaltyAudit(keptRoyalty);
            await dbContext.RoyaltyAudits.CreateAsync(cascadedAudit);
            await dbContext.RoyaltyAudits.CreateAsync(keptAudit);

            // Action.
            await dbContext.Tracks.DeleteAsync(deletedTrack);
            await ExecutePendingTasksAsync();

            // Assert: the whole chain of the deleted track is gone, the other one is intact.
            Assert.Null(await GetRawDocumentOrDefaultAsync("royalties", cascadedRoyalty.Id));
            Assert.Null(await GetRawDocumentOrDefaultAsync("royaltyAudits", cascadedAudit.Id));
            Assert.NotNull(await GetRawDocumentOrDefaultAsync("royalties", keptRoyalty.Id));
            Assert.NotNull(await GetRawDocumentOrDefaultAsync("royaltyAudits", keptAudit.Id));
            Assert.NotNull(await GetRawDocumentOrDefaultAsync("tracks", keptTrack.Id));
        }

        [Fact]
        public async Task CascadeDeleteTerminatesOnMutualReferences()
        {
            /* Two documents referencing each other with the referencing document delete
             * policy form a reference cycle: the cascade closes it on the documents already
             * deleted — every step only deletes still existing documents, and what a step
             * deleted doesn't match the next lookup anymore — instead of requeuing forever. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await ResetPropagationCollectionsAsync();

            var first = new Duet("first");
            await dbContext.Duets.CreateAsync(first);
            var second = new Duet("second") { Partner = first };
            await dbContext.Duets.CreateAsync(second);

            //close the cycle
            var loadedFirst = await dbContext.Duets.FindOneAsync(first.Id);
            loadedFirst.Partner = second;
            await dbContext.SaveChangesAsync();
            fixture.TaskRunner.ClearPending();

            // Action.
            await dbContext.Duets.DeleteAsync(loadedFirst);
            await ExecutePendingTasksAsync();

            // Assert: the whole cycle is gone, and the drain terminated.
            Assert.Null(await GetRawDocumentOrDefaultAsync("duets", first.Id));
            Assert.Null(await GetRawDocumentOrDefaultAsync("duets", second.Id));
            Assert.Equal(0, fixture.TaskRunner.PendingCount);
        }

        [Fact]
        public async Task RawBulkDeleteDoesNotPropagate()
        {
            /* DeleteManyAsync is a raw bulk operation, out of the domain flows: nothing
             * enqueues, and the references stay on their documents, found by the missing
             * origin references scan. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await ResetPropagationCollectionsAsync();
            fixture.TaskRunner.ClearPending();

            var track = new Track("raw deleted");
            var mixtape = new Mixtape("mixtape") { Highlight = track };
            await dbContext.Mixtapes.CreateAsync(mixtape);

            // Action.
            await dbContext.Tracks.DeleteManyAsync(t => t.Id == track.Id);

            // Assert.
            Assert.Equal(0, fixture.TaskRunner.PendingCount);
            var rawMixtape = await GetRawDocumentAsync("mixtapes", mixtape.Id);
            Assert.Equal(ObjectId.Parse(track.Id), rawMixtape["Highlight"]["_id"].AsObjectId);
        }

        [Fact]
        public async Task ExplicitKeepReferenceOptsOutOfThePropagation()
        {
            /* A reference declaring to keep the reference opts out of the delete
             * propagation: its summary stays dangling on the documents, read per the
             * declared missing origin document mode, while the references following the
             * default on the same document get removed. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await ResetPropagationCollectionsAsync();
            fixture.TaskRunner.ClearPending();

            var track = new Track("deleted");
            var mixtape = new Mixtape("mixtape")
            {
                Highlight = track,
                Pinned = track
            };
            await dbContext.Mixtapes.CreateAsync(mixtape);

            // Action.
            await dbContext.Tracks.DeleteAsync(track);
            await ExecutePendingTasksAsync();

            // Assert.
            var rawMixtape = await GetRawDocumentAsync("mixtapes", mixtape.Id);
            Assert.Equal(BsonNull.Value, rawMixtape["Highlight"]);
            Assert.Equal(ObjectId.Parse(track.Id), rawMixtape["Pinned"]["_id"].AsObjectId);
        }

        [Fact]
        public async Task MismatchedDeletedDbContextTypesAreSkipped()
        {
            /* The deleted model repository is identified by db context type and repository
             * name together, since repository names are unique per db context only: a
             * payload carrying the type of another db context matches no reference and
             * skips, without touching the documents. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await ResetPropagationCollectionsAsync();
            fixture.TaskRunner.ClearPending();

            var track = new Track("referenced");
            var mixtape = new Mixtape("mixtape") { Highlight = track };
            await dbContext.Mixtapes.CreateAsync(mixtape);

            var idMemberMapIds = dbContext.Engine.MapRegistry.MemberMapsById.Values
                .Where(memberMap => memberMap is { IsEntityReferenceMember: true, IsIdMember: true })
                .Select(memberMap => memberMap.Id)
                .ToArray();

            // Action: run the task directly, with the type of another db context.
            using var taskScope = fixture.ServiceProvider.CreateScope();
            var task = taskScope.ServiceProvider.GetRequiredService<IDeleteDocDependenciesTask>();
            await task.RunAsync<TestDbContext>(typeof(SecondDbContext), "tracks", track.Id, idMemberMapIds);

            // Assert: the reference is untouched.
            var rawMixtape = await GetRawDocumentAsync("mixtapes", mixtape.Id);
            Assert.Equal(ObjectId.Parse(track.Id), rawMixtape["Highlight"]["_id"].AsObjectId);

            // Action and assert: the same payload with the right type removes the reference.
            await task.RunAsync<TestDbContext>(typeof(TestDbContext), "tracks", track.Id, idMemberMapIds);

            rawMixtape = await GetRawDocumentAsync("mixtapes", mixtape.Id);
            Assert.Equal(BsonNull.Value, rawMixtape["Highlight"]);
        }

        // Helpers.
        /* Drain the pending propagation tasks: a cascade delete enqueues the propagation of
         * the documents it deletes, so one execution round can enqueue the next. */
        private async Task ExecutePendingTasksAsync()
        {
            while (fixture.TaskRunner.PendingCount > 0)
                await fixture.TaskRunner.ExecutePendingAsync(fixture.ServiceProvider);
        }

        private async Task<BsonDocument> GetRawDocumentAsync(string collectionName, string id) =>
            (await GetRawDocumentOrDefaultAsync(collectionName, id))!;

        private async Task<BsonDocument?> GetRawDocumentOrDefaultAsync(string collectionName, string id)
        {
            var collection = dbContext.Engine.Database.GetCollection<BsonDocument>(collectionName);
            return await collection
                .Find(Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(id)))
                .FirstOrDefaultAsync();
        }

        /* The propagation collections are used by these tests only: purging them keeps the
         * assertions independent from the execution order. */
        private async Task ResetPropagationCollectionsAsync()
        {
            foreach (var collectionName in new[] { "duets", "mixtapes", "royalties", "royaltyAudits", "tracks" })
            {
                var collection = dbContext.Engine.Database.GetCollection<BsonDocument>(collectionName);
                await collection.DeleteManyAsync(Builders<BsonDocument>.Filter.Empty);
            }
        }
    }
}
