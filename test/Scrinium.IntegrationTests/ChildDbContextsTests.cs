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
    public class ChildDbContextsTests : IDisposable
    {
        // Fields.
        private readonly IntegrationFixture fixture;
        private readonly IParentDbContext parentDbContext;
        private readonly ISecondDbContext secondDbContext;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public ChildDbContextsTests(IntegrationFixture fixture)
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
        public async Task RepeatedChildLoadsAccumulateChangesForParentSave()
        {
            /* The pattern of identity stores over shared models: the same child document is
             * loaded multiple times inside one operation, mutated through any of the returned
             * references, and persisted only by the parent context save. Requires instance
             * deduplication on the child plus the children save cascading. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var note = new Note("text");
            await secondDbContext.Notes.CreateAsync(note);

            var firstLoadedNote = await secondDbContext.Notes.FindOneAsync(note.Id);
            var secondLoadedNote = await secondDbContext.Notes.FindOneAsync(note.Id);
            Assert.Same(firstLoadedNote, secondLoadedNote);

            // Action.
            secondLoadedNote.Text = "updated through second reference";
            await parentDbContext.SaveChangesAsync();

            // Assert.
            using var readScope = fixture.ServiceProvider.CreateScope();
            var readSecondDbContext = readScope.ServiceProvider.GetRequiredService<ISecondDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            var foundNote = await readSecondDbContext.Notes.FindOneAsync(note.Id);
            Assert.Equal("updated through second reference", foundNote.Text);
        }

        [Fact]
        public async Task SaveChangesCascadesToChildDbContexts()
        {
            /* The parent db context attaches the child instances resolved from its same DI
             * scope: saving the parent must persist also the changed models of the children. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var note = new Note("text");
            await secondDbContext.Notes.CreateAsync(note);

            var loadedNote = await secondDbContext.Notes.FindOneAsync(note.Id);
            loadedNote.Text = "updated by parent save";

            // Action.
            await parentDbContext.SaveChangesAsync();

            // Assert.
            //read the db state through a fresh scope, not deduplicated with local instances
            using var readScope = fixture.ServiceProvider.CreateScope();
            var readSecondDbContext = readScope.ServiceProvider.GetRequiredService<ISecondDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            var foundNote = await readSecondDbContext.Notes.FindOneAsync(note.Id);
            Assert.Equal("updated by parent save", foundNote.Text);
        }
    }
}
