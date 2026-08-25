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
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Domain.Models.DbMigrationOpAgg;
using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Etherna.Scrinium.IntegrationTests.Fixtures;
using Etherna.Scrinium.IntegrationTests.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.IntegrationTests
{
    [Collection("Integration")]
    public class ReadOnlyAccessTests : IDisposable
    {
        // Fields.
        private readonly IMixedAccessDbContext mixedAccessDbContext;
        private readonly IReadOnlyDbContext readOnlyDbContext;
        private readonly ISecondDbContext secondDbContext;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public ReadOnlyAccessTests(IntegrationFixture fixture)
        {
            serviceScope = fixture.ServiceProvider.CreateScope();
            mixedAccessDbContext = serviceScope.ServiceProvider.GetRequiredService<IMixedAccessDbContext>();
            readOnlyDbContext = serviceScope.ServiceProvider.GetRequiredService<IReadOnlyDbContext>();
            secondDbContext = serviceScope.ServiceProvider.GetRequiredService<ISecondDbContext>();
        }

        public void Dispose()
        {
            serviceScope.Dispose();
            GC.SuppressFinalize(this);
        }

        // Tests.
        [Fact]
        public async Task MigrationSkipsIndexStepsOnReadOnlyRepositories()
        {
            // Setup.
            /* The fixture seeded the mixed access db context, running its migration
             * at initialization. */
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            // Action.
            var migrations = await mixedAccessDbContext.GetLastMigrationsAsync(0, 10);

            // Assert.
            //the shared read-only collection stays out of the index steps
            var migration = Assert.Single(migrations);
            Assert.Equal(DbMigrationOperation.Status.Completed, migration.CurrentStatus);
            Assert.DoesNotContain(migration.Logs, log => log is DeleteOldIndexesMigrationLog { Repository: "notes" });
            Assert.DoesNotContain(migration.Logs, log => log is BuildNewIndexesMigrationLog { Repository: "notes" });
            Assert.Contains(migration.Logs, log => log is DeleteOldIndexesMigrationLog { Repository: "mixedTagBags" });
            Assert.Contains(migration.Logs, log => log is BuildNewIndexesMigrationLog { Repository: "mixedTagBags" });
        }

        [Fact]
        public async Task MixedAccessDbContextDeniesWritesOnlyOnReadOnlyRepository()
        {
            // Setup.
            using var setupContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var note = new Note("shared text");
            await secondDbContext.Notes.CreateAsync(note);

            // Action and assert.
            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            //the writable repository works normally
            var tagBag = new TagBag();
            await mixedAccessDbContext.TagBags.CreateAsync(tagBag);
            var foundTagBag = await mixedAccessDbContext.TagBags.FindOneAsync(tagBag.Id);
            Assert.Equal(tagBag.Id, foundTagBag.Id);

            //the read-only repository reads the shared data, and denies writes
            var foundNote = await mixedAccessDbContext.Notes.FindOneAsync(note.Id);
            Assert.Equal("shared text", foundNote.Text);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                mixedAccessDbContext.Notes.CreateAsync(new Note("denied")));
        }

        [Fact]
        public async Task ReadOnlyDbContextAllowsReads()
        {
            // Setup.
            using var setupContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var note = new Note("shared text");
            await secondDbContext.Notes.CreateAsync(note);

            // Action.
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var foundNote = await readOnlyDbContext.Notes.FindOneAsync(note.Id);
            var queriedNote = await readOnlyDbContext.Notes.QueryElementsAsync(elements =>
                elements.Where(n => n.Id == note.Id)
                        .FirstAsync());

            // Assert.
            Assert.Equal("shared text", foundNote.Text);
            Assert.Equal(note.Id, queriedNote.Id);
        }

        [Fact]
        public async Task ReadOnlyDbContextDeniesIndexManagement()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            // Action and assert.
            //index listing is a read, and keeps working
            await readOnlyDbContext.Notes.AccessToCollectionAsync(async collection =>
            {
                using var indexes = await collection.Indexes.ListAsync();
                await indexes.ToListAsync();
            });

            //index creation is a write, and is denied
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                readOnlyDbContext.Notes.AccessToCollectionAsync(collection =>
                    collection.Indexes.CreateOneAsync(new CreateIndexModel<Note>(
                        Builders<Note>.IndexKeys.Ascending(n => n.Text)))));
        }

        [Fact]
        public async Task ReadOnlyDbContextDeniesStartingMigrations()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            // Action.
            var migrationOp = await readOnlyDbContext.TryStartMigrationAsync();

            // Assert.
            Assert.Null(migrationOp);
        }

        [Fact]
        public async Task ReadOnlyDbContextDeniesWrites()
        {
            // Setup.
            using var setupContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var note = new Note("owner text");
            await secondDbContext.Notes.CreateAsync(note);

            // Action and assert.
            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            //create
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                readOnlyDbContext.Notes.CreateAsync(new Note("denied")));

            //save changes of a mutated loaded model
            var loadedNote = await readOnlyDbContext.Notes.FindOneAsync(note.Id);
            loadedNote.Text = "mutated";
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                readOnlyDbContext.SaveChangesAsync());

            //delete
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                readOnlyDbContext.Notes.DeleteAsync(loadedNote));

            //raw bulk operations
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                readOnlyDbContext.Notes.UpdateManyAsync(
                    n => n.Id == note.Id,
                    Builders<Note>.Update.Set(n => n.Text, "denied")));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                readOnlyDbContext.Notes.DeleteManyAsync(n => n.Id == note.Id));

            //the owner document is untouched
            using var verifyContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var foundNote = await secondDbContext.Notes.FindOneAsync(note.Id);
            Assert.Equal("owner text", foundNote.Text);
        }

        [Fact]
        public async Task ReadOnlyDbContextDeniesWritesThroughDatabase()
        {
            // Setup.
            using var setupContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var note = new Note("owner text");
            await secondDbContext.Notes.CreateAsync(note);

            // Action and assert.
            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await readOnlyDbContext.Notes.AccessToCollectionAsync(async collection =>
            {
                var database = collection.Database;

                //writes on a database retrieved collection
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    database.GetCollection<Note>("notes").DeleteManyAsync(Builders<Note>.Filter.Empty));

                //database level writes
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    database.DropCollectionAsync("notes"));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    database.RenameCollectionAsync("notes", "renamedNotes"));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    database.RunCommandAsync(new BsonDocumentCommand<BsonDocument>(new BsonDocument("dropDatabase", 1))));
            });

            //the owner document is untouched
            using var verifyContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var foundNote = await secondDbContext.Notes.FindOneAsync(note.Id);
            Assert.Equal("owner text", foundNote.Text);
        }

        [Fact]
        public async Task ReadOnlyDbContextDeniesWritesThroughOfType()
        {
            // Setup.
            using var setupContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var note = new Note("owner text");
            await secondDbContext.Notes.CreateAsync(note);

            // Action and assert.
            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                readOnlyDbContext.Notes.AccessToCollectionAsync(collection =>
                    collection.OfType<Note>().DeleteManyAsync(Builders<Note>.Filter.Empty)));

            //the owner document is untouched
            using var verifyContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var foundNote = await secondDbContext.Notes.FindOneAsync(note.Id);
            Assert.Equal("owner text", foundNote.Text);
        }

        [Fact]
        public async Task ReadOnlyDbContextSkipsSeeding()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            // Action.
            var seeded = await readOnlyDbContext.SeedIfNeededAsync();

            // Assert.
            //no seeding executed, and no seed operation persisted for the read-only db context
            Assert.False(seeded);
            var seedOperationsCount = await readOnlyDbContext.DbOperations.QueryElementsAsync(elements =>
                elements.OfType<SeedOperation>()
                        .Where(op => op.DbContextName == readOnlyDbContext.Engine.Identifier)
                        .CountAsync());
            Assert.Equal(0, seedOperationsCount);
        }

        [Fact]
        public void RepositoriesReportTheEffectiveReadOnlyFlag()
        {
            //by db context options
            Assert.True(readOnlyDbContext.Notes.IsReadOnly);
            Assert.True(readOnlyDbContext.DbOperations.IsReadOnly);

            //by repository options
            Assert.True(mixedAccessDbContext.Notes.IsReadOnly);
            Assert.False(mixedAccessDbContext.TagBags.IsReadOnly);

            //writable everywhere
            Assert.False(secondDbContext.Notes.IsReadOnly);
        }
    }
}
