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
using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.Domain.Models.DbMigrationOpAgg;
using Etherna.MongODM.Core.ExecContext.AsyncLocal;
using Etherna.MongODM.Core.Migration;
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
    public class DbMigrationTests : IDisposable
    {
        // Fields.
        private readonly IntegrationFixture fixture;
        private readonly IMigrationsDbContext migrationsDbContext;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public DbMigrationTests(IntegrationFixture fixture)
        {
            this.fixture = fixture;
            serviceScope = fixture.ServiceProvider.CreateScope();
            migrationsDbContext = serviceScope.ServiceProvider.GetRequiredService<IMigrationsDbContext>();
        }

        public void Dispose()
        {
            serviceScope.Dispose();
            GC.SuppressFinalize(this);
        }

        // Tests.
        [Fact]
        public async Task DryRunMigrationExecutesCustomProcessorWithoutPersisting()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await migrationsDbContext.Notes.DeleteManyAsync(Builders<Note>.Filter.Empty);
            fixture.TaskRunner.ClearPending();

            var firstNote = new Note("first");
            var secondNote = new Note("second");
            await migrationsDbContext.Notes.CreateAsync(firstNote);
            await migrationsDbContext.Notes.CreateAsync(secondNote);

            var processedNotes = 0;
            migrationsDbContext.DocumentMigrations =
            [
                new DocumentMigration<Note, string>(migrationsDbContext.Notes, async note =>
                {
                    processedNotes++;
                    note.Tag = "processed";
                    await migrationsDbContext.SaveChangesAsync();
                })
            ];

            var documentsBefore = await ListRawNoteDocumentsAsync();

            // Action.
            var migrationOp = await migrationsDbContext.TryStartMigrationAsync(dryRun: true);
            Assert.NotNull(migrationOp);
            await migrationsDbContext.ExecuteMigrationAsync(migrationOp.Id);

            // Assert.
            //the custom processor executed on every document, but nothing is persisted
            Assert.Equal(2, processedNotes);
            Assert.Equal(documentsBefore, await ListRawNoteDocumentsAsync());

            //no dependencies update task is enqueued by the simulated note saves
            /* The operation saves on DbOperations enqueue their own dependencies updates:
             * only the dry run scope of the document processing suppresses the propagation. */
            Assert.DoesNotContain(fixture.TaskRunner.PendingModelIds, id => Equals(id, firstNote.Id));
            Assert.DoesNotContain(fixture.TaskRunner.PendingModelIds, id => Equals(id, secondNote.Id));

            var completedOp = await migrationsDbContext.GetMigrationAsync(migrationOp.Id);
            Assert.Equal(DbMigrationOperation.Status.Completed, completedOp.CurrentStatus);
            Assert.Contains(completedOp.Logs, log => log is DocumentMigrationLog
            {
                State: MigrationLogBase.ExecutionState.Succeded,
                TotMigratedDocs: 2,
                TotErrorDocs: 0
            });
        }

        [Fact]
        public async Task DryRunMigrationReportsFailingDocumentsWithoutPersisting()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await migrationsDbContext.Notes.DeleteManyAsync(Builders<Note>.Filter.Empty);

            var note = new Note("valid text");
            await migrationsDbContext.Notes.CreateAsync(note);

            //persist a raw document breaking deserialization: a document value on the string member
            BsonDocument brokenDocument = null!;
            await migrationsDbContext.Notes.AccessToCollectionAsync(async collection =>
            {
                var rawCollection = collection.Database.GetCollection<BsonDocument>(
                    collection.CollectionNamespace.CollectionName);
                var validDocument = await (await rawCollection.FindAsync(FilterDefinition<BsonDocument>.Empty)).SingleAsync();

                brokenDocument = (BsonDocument)validDocument.DeepClone();
                brokenDocument["_id"] = ObjectId.GenerateNewId();
                var textElementName = brokenDocument.Elements.Single(e => e.Value == "valid text").Name;
                brokenDocument[textElementName] = new BsonDocument("broken", true);
                await rawCollection.InsertOneAsync(brokenDocument);
            });

            migrationsDbContext.DocumentMigrations =
                [new DocumentMigration<Note, string>(migrationsDbContext.Notes)];

            var documentsBefore = await ListRawNoteDocumentsAsync();

            // Action.
            var migrationOp = await migrationsDbContext.TryStartMigrationAsync(dryRun: true);
            Assert.NotNull(migrationOp);
            Assert.True(migrationOp.IsDryRun);
            await migrationsDbContext.ExecuteMigrationAsync(migrationOp.Id);

            // Assert.
            //nothing is persisted, the broken document included
            Assert.Equal(documentsBefore, await ListRawNoteDocumentsAsync());

            //the completed operation reports the failing document, reading from a fresh scope
            using var verifyScope = fixture.ServiceProvider.CreateScope();
            var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<IMigrationsDbContext>();
            var completedOp = await verifyDbContext.GetMigrationAsync(migrationOp.Id);
            Assert.True(completedOp.IsDryRun);
            Assert.Equal(DbMigrationOperation.Status.Failed, completedOp.CurrentStatus);
            Assert.DoesNotContain(completedOp.Logs, log => log is DeleteOldIndexesMigrationLog or BuildNewIndexesMigrationLog);
            var documentLog = Assert.IsType<DocumentMigrationLog>(Assert.Single(completedOp.Logs));
            Assert.Equal(MigrationLogBase.ExecutionState.Failed, documentLog.State);
            Assert.Equal(2, documentLog.TotMigratedDocs);
            Assert.Equal(1, documentLog.TotErrorDocs);
            var documentError = Assert.Single(documentLog.Errors);
            Assert.Equal(brokenDocument["_id"].ToString(), documentError.DocumentId);
            Assert.NotEmpty(documentError.Message);
        }

        [Fact]
        public async Task MigrationPersistsProcessedDocuments()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await migrationsDbContext.Notes.DeleteManyAsync(Builders<Note>.Filter.Empty);
            fixture.TaskRunner.ClearPending();

            var note = new Note("first");
            await migrationsDbContext.Notes.CreateAsync(note);

            migrationsDbContext.DocumentMigrations =
            [
                new DocumentMigration<Note, string>(migrationsDbContext.Notes, async n =>
                {
                    n.Tag = "migrated";
                    await migrationsDbContext.SaveChangesAsync();
                })
            ];

            // Action.
            var migrationOp = await migrationsDbContext.TryStartMigrationAsync();
            Assert.NotNull(migrationOp);
            Assert.False(migrationOp.IsDryRun);
            await migrationsDbContext.ExecuteMigrationAsync(migrationOp.Id);

            // Assert.
            //the migration persisted the processed documents, and ran the index steps
            using var verifyScope = fixture.ServiceProvider.CreateScope();
            var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<IMigrationsDbContext>();
            var migratedNote = await verifyDbContext.Notes.FindOneAsync(note.Id);
            Assert.Equal("migrated", migratedNote.Tag);

            var completedOp = await verifyDbContext.GetMigrationAsync(migrationOp.Id);
            Assert.Equal(DbMigrationOperation.Status.Completed, completedOp.CurrentStatus);
            Assert.Contains(completedOp.Logs, log => log is DeleteOldIndexesMigrationLog { Repository: "notes" });
            Assert.Contains(completedOp.Logs, log => log is BuildNewIndexesMigrationLog { Repository: "notes" });
            Assert.Contains(completedOp.Logs, log => log is DocumentMigrationLog
            {
                State: MigrationLogBase.ExecutionState.Succeded,
                TotMigratedDocs: 1,
                TotErrorDocs: 0
            });

            //the real save propagates its dependencies update, unlike the dry run one
            Assert.Contains(fixture.TaskRunner.PendingModelIds, id => Equals(id, note.Id));
            fixture.TaskRunner.ClearPending();
        }

        // Helpers.
        private Task<List<BsonDocument>> ListRawNoteDocumentsAsync() =>
            migrationsDbContext.Notes.AccessToCollectionAsync(async collection =>
            {
                var rawCollection = collection.Database.GetCollection<BsonDocument>(
                    collection.CollectionNamespace.CollectionName);
                return await (await rawCollection.FindAsync(
                    FilterDefinition<BsonDocument>.Empty,
                    new FindOptions<BsonDocument> { Sort = Builders<BsonDocument>.Sort.Ascending("_id") })).ToListAsync();
            });
    }
}
