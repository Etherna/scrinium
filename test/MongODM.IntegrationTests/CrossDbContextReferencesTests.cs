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

using Etherna.MongODM.Core.ExecContext.AsyncLocal;
using Etherna.MongODM.Core.ProxyModels;
using Etherna.MongODM.IntegrationTests.Fixtures;
using Etherna.MongODM.IntegrationTests.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.MongODM.IntegrationTests
{
    [Collection("Integration")]
    public class CrossDbContextReferencesTests : IDisposable
    {
        // Fields.
        private readonly IntegrationFixture fixture;
        private readonly IParentDbContext parentDbContext;
        private readonly ISecondDbContext secondDbContext;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public CrossDbContextReferencesTests(IntegrationFixture fixture)
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
        public async Task CrossDbContextReferenceBindsDeclaredChildSource()
        {
            /* A reference member declaring its typed source on the child db context binds
             * the child repository at deserialization: the summary carries the denormalized
             * members, and lazy loads the missing ones from the child collection. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var note = new Note("note text") { Tag = "note tag" };
            await secondDbContext.Notes.CreateAsync(note);
            var journal = new Journal("journal title") { PinnedNote = note };
            await parentDbContext.Journals.CreateAsync(journal);

            // Action.
            using var readScope = fixture.ServiceProvider.CreateScope();
            var readParentDbContext = readScope.ServiceProvider.GetRequiredService<IParentDbContext>();
            var readSecondDbContext = readScope.ServiceProvider.GetRequiredService<ISecondDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            var loadedJournal = await readParentDbContext.Journals.FindOneAsync(journal.Id);
            var referencedNote = loadedJournal.PinnedNote!;

            // Assert.
            Assert.Same(readSecondDbContext.Notes, ((IReferenceable)referencedNote).SourceRepository);
            Assert.Equal("note tag", referencedNote.Tag); //denormalized on the summary
            Assert.Equal("note text", referencedNote.Text); //lazy loads from the child collection
        }

        [Fact]
        public async Task CrossDbContextSummaryDeduplicatesWithDirectChildLoad()
        {
            /* The identity home of a cross db context reference is the child db context
             * owning its source repository: a document loaded from the child repository and
             * then referenced by a parent document materializes one single instance. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var note = new Note("note text");
            await secondDbContext.Notes.CreateAsync(note);
            var journal = new Journal("journal title") { PinnedNote = note };
            await parentDbContext.Journals.CreateAsync(journal);

            // Action.
            using var readScope = fixture.ServiceProvider.CreateScope();
            var readParentDbContext = readScope.ServiceProvider.GetRequiredService<IParentDbContext>();
            var readSecondDbContext = readScope.ServiceProvider.GetRequiredService<ISecondDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            var directNote = await readSecondDbContext.Notes.FindOneAsync(note.Id);
            var loadedJournal = await readParentDbContext.Journals.FindOneAsync(journal.Id);

            // Assert.
            Assert.Same(directNote, loadedJournal.PinnedNote);
        }

        [Fact]
        public async Task DirectChildLoadUpgradesCrossDbContextSummary()
        {
            /* The reverse deduplication direction: the summary referenced by the parent
             * document upgrades in place when the child repository loads its full document,
             * staying the one single instance of the scope. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var note = new Note("note text");
            await secondDbContext.Notes.CreateAsync(note);
            var journal = new Journal("journal title") { PinnedNote = note };
            await parentDbContext.Journals.CreateAsync(journal);

            // Action.
            using var readScope = fixture.ServiceProvider.CreateScope();
            var readParentDbContext = readScope.ServiceProvider.GetRequiredService<IParentDbContext>();
            var readSecondDbContext = readScope.ServiceProvider.GetRequiredService<ISecondDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            var loadedJournal = await readParentDbContext.Journals.FindOneAsync(journal.Id);
            var referencedNote = loadedJournal.PinnedNote!;
            var directNote = await readSecondDbContext.Notes.FindOneAsync(note.Id);

            // Assert.
            Assert.Same(referencedNote, directNote);

            //the summary upgraded in place with the full document, without lazy loads
            Assert.True(readSecondDbContext.IsMemberLoaded(referencedNote, n => n.Text));
            Assert.Equal("note text", referencedNote.Text);
        }

        [Fact]
        public async Task CrossDbContextReferencedModelSavesThroughParentCascade()
        {
            /* A referenced child model mutated through the parent document graph tracks its
             * changes on the child db context: the parent save cascades to the children,
             * persisting them without touching the child context explicitly. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var note = new Note("note text");
            await secondDbContext.Notes.CreateAsync(note);
            var journal = new Journal("journal title") { PinnedNote = note };
            await parentDbContext.Journals.CreateAsync(journal);

            using var workScope = fixture.ServiceProvider.CreateScope();
            var workParentDbContext = workScope.ServiceProvider.GetRequiredService<IParentDbContext>();
            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            var loadedJournal = await workParentDbContext.Journals.FindOneAsync(journal.Id);

            // Action.
            loadedJournal.PinnedNote!.Text = "updated through parent graph";
            await workParentDbContext.SaveChangesAsync();

            // Assert.
            //read the db state through a fresh scope, not deduplicated with local instances
            using var readScope = fixture.ServiceProvider.CreateScope();
            var readSecondDbContext = readScope.ServiceProvider.GetRequiredService<ISecondDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            var foundNote = await readSecondDbContext.Notes.FindOneAsync(note.Id);
            Assert.Equal("updated through parent graph", foundNote.Text);
        }

        [Fact]
        public async Task CrossDbContextNewReferredModelAutoCreates()
        {
            /* An entity model referred with a null id is a new model: a parent repository
             * write serializing its reference auto creates it into the child db context
             * source repository, before persisting the referencing document. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var note = new Note("auto created text");
            var journal = new Journal("journal title") { PinnedNote = note };

            // Action.
            await parentDbContext.Journals.CreateAsync(journal);

            // Assert.
            Assert.NotNull(note.Id);

            using var readScope = fixture.ServiceProvider.CreateScope();
            var readParentDbContext = readScope.ServiceProvider.GetRequiredService<IParentDbContext>();
            var readSecondDbContext = readScope.ServiceProvider.GetRequiredService<ISecondDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            var foundNote = await readSecondDbContext.Notes.FindOneAsync(note.Id);
            Assert.Equal("auto created text", foundNote.Text);

            var foundJournal = await readParentDbContext.Journals.FindOneAsync(journal.Id);
            Assert.Equal(note.Id, foundJournal.PinnedNote!.Id);
        }
    }
}
