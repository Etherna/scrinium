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
    public class CrossDbContextPropagationTests : IDisposable
    {
        // Fields.
        private readonly IntegrationFixture fixture;
        private readonly IParentDbContext parentDbContext;
        private readonly ISecondDbContext secondDbContext;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public CrossDbContextPropagationTests(IntegrationFixture fixture)
        {
            this.fixture = fixture;
            serviceScope = fixture.ServiceProvider.CreateScope();
            parentDbContext = serviceScope.ServiceProvider.GetRequiredService<IParentDbContext>();
            secondDbContext = serviceScope.ServiceProvider.GetRequiredService<ISecondDbContext>();
        }

        public void Dispose()
        {
            serviceScope.Dispose();
            GC.SuppressFinalize(this);
        }

        // Tests.
        [Fact]
        public async Task ChangedChildModelThroughParentCascadeUpdatesSummaries()
        {
            /* The typical flow of a shared model: the child model mutates through the
             * parent document graph, and the parent save cascades it to the child db
             * context. The cascaded child save enqueues the parent propagation like a
             * direct child save does. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var note = new Note("note text") { Tag = "original tag" };
            await secondDbContext.Notes.CreateAsync(note);
            var journal = new Journal("journal title") { PinnedNote = note };
            await parentDbContext.Journals.CreateAsync(journal);

            using var workScope = fixture.ServiceProvider.CreateScope();
            var workParentDbContext = workScope.ServiceProvider.GetRequiredService<IParentDbContext>();
            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            var loadedJournal = await workParentDbContext.Journals.FindOneAsync(journal.Id);
            fixture.TaskRunner.ClearPending();

            // Action.
            loadedJournal.PinnedNote!.Tag = "cascade tag";
            await workParentDbContext.SaveChangesAsync();

            // Assert.
            Assert.Equal(1, fixture.TaskRunner.PendingCount);
            await fixture.TaskRunner.ExecutePendingAsync(fixture.ServiceProvider);

            var rawJournal = await TryFindRawJournalAsync(journal.Id);
            Assert.Equal("cascade tag", rawJournal!["PinnedNote"]["Tag"].AsString);
        }

        [Fact]
        public async Task ChangedChildModelUpdatesSummariesOnParentDbContextDocuments()
        {
            /* SCR-243: a change of the child model enqueues one dependencies update task
             * for each parent db context of the application denormalizing the changed
             * members — the writable parent only, since the read-only parent consumes
             * documents owned by another application — and the task refreshes the
             * summaries on the parent documents. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var note = new Note("note text") { Tag = "original tag" };
            await secondDbContext.Notes.CreateAsync(note);
            var journal = new Journal("journal title") { PinnedNote = note };
            await parentDbContext.Journals.CreateAsync(journal);

            // Action: update the denormalized member through the child repository.
            fixture.TaskRunner.ClearPending();
            var loadedNote = await secondDbContext.Notes.FindOneAsync(note.Id);
            loadedNote.Tag = "updated tag";
            await secondDbContext.SaveChangesAsync();

            // Assert: one task toward the writable parent db context refreshes the summary.
            Assert.Equal(1, fixture.TaskRunner.PendingCount);
            await fixture.TaskRunner.ExecutePendingAsync(fixture.ServiceProvider);

            var rawJournal = await TryFindRawJournalAsync(journal.Id);
            Assert.Equal("updated tag", rawJournal!["PinnedNote"]["Tag"].AsString);
        }

        [Fact]
        public async Task DeletedChildModelCascadesToParentDbContextReferencingDocuments()
        {
            /* A reference declaring the referencing document delete applies its policy
             * also across the engines: the parent documents referencing the deleted child
             * model delete with a domain delete, while the paths of the references
             * declaring the default removal repair apart. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var note = new Note("note text");
            await secondDbContext.Notes.CreateAsync(note);
            var aboutJournal = new Journal("journal about the note") { SubjectNote = note };
            await parentDbContext.Journals.CreateAsync(aboutJournal);
            var pinningJournal = new Journal("journal pinning the note") { PinnedNote = note };
            await parentDbContext.Journals.CreateAsync(pinningJournal);

            // Action.
            fixture.TaskRunner.ClearPending();
            await secondDbContext.Notes.DeleteAsync(note.Id);

            Assert.Equal(1, fixture.TaskRunner.PendingCount);
            await fixture.TaskRunner.ExecutePendingAsync(fixture.ServiceProvider);

            // Assert.
            Assert.Null(await TryFindRawJournalAsync(aboutJournal.Id));

            var rawPinningJournal = await TryFindRawJournalAsync(pinningJournal.Id);
            Assert.NotNull(rawPinningJournal);
            Assert.True(rawPinningJournal["PinnedNote"].IsBsonNull);
        }

        [Fact]
        public async Task DeletedChildModelRemovesReferencesOnParentDbContextDocuments()
        {
            /* SCR-243: a domain delete of the child model enqueues one dependencies
             * delete task for each parent db context of the application declaring an
             * origin delete policy on it, and the task removes the references from the
             * parent documents — only from the ones referencing the deleted model. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var deletedNote = new Note("deleted note text");
            await secondDbContext.Notes.CreateAsync(deletedNote);
            var otherNote = new Note("other note text") { Tag = "other tag" };
            await secondDbContext.Notes.CreateAsync(otherNote);

            var referencingJournal = new Journal("journal of the deleted note") { PinnedNote = deletedNote };
            await parentDbContext.Journals.CreateAsync(referencingJournal);
            var untouchedJournal = new Journal("journal of the other note") { PinnedNote = otherNote };
            await parentDbContext.Journals.CreateAsync(untouchedJournal);

            // Action.
            fixture.TaskRunner.ClearPending();
            await secondDbContext.Notes.DeleteAsync(deletedNote.Id);

            Assert.Equal(1, fixture.TaskRunner.PendingCount);
            await fixture.TaskRunner.ExecutePendingAsync(fixture.ServiceProvider);

            // Assert.
            var rawReferencingJournal = await TryFindRawJournalAsync(referencingJournal.Id);
            Assert.NotNull(rawReferencingJournal);
            Assert.True(rawReferencingJournal["PinnedNote"].IsBsonNull);

            var rawUntouchedJournal = await TryFindRawJournalAsync(untouchedJournal.Id);
            Assert.Equal(otherNote.Id, rawUntouchedJournal!["PinnedNote"]["_id"].AsObjectId.ToString());
        }

        // Helpers.
        private async Task<BsonDocument?> TryFindRawJournalAsync(string journalId)
        {
            var journalsCollection = parentDbContext.Engine.Database.GetCollection<BsonDocument>("journals");
            return await journalsCollection
                .Find(Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(journalId)))
                .SingleOrDefaultAsync();
        }
    }
}
