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
        public async Task DryRunMigrationDeniesNonSimulableCollectionWrites()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await migrationsDbContext.Notes.DeleteManyAsync(Builders<Note>.Filter.Empty);

            await migrationsDbContext.Notes.CreateAsync(new Note("text"));

            /* The processor captures the guard exceptions itself: under the ambient dry run
             * the collection must deny the writes it can't simulate, before any command
             * reaches the server. */
            Exception? aggregateException = null;
            Exception? mapReduceException = null;
            migrationsDbContext.DocumentMigrations =
            [
                new DocumentMigration<Note, string>(migrationsDbContext.Notes, _ =>
                    migrationsDbContext.Notes.AccessToCollectionAsync(async collection =>
                    {
                        aggregateException = await Record.ExceptionAsync(() => collection.AggregateAsync(
                            PipelineDefinition<Note, BsonDocument>.Create(
                                new BsonDocument("$out", "dryRunAggregateTarget"))));
#pragma warning disable CS0618 //map reduce stays guarded while the driver exposes it
                        mapReduceException = await Record.ExceptionAsync(() => collection.MapReduceAsync(
                            new BsonJavaScript("function() { emit(this._id, 1); }"),
                            new BsonJavaScript("function(key, values) { return Array.sum(values); }"),
                            new MapReduceOptions<Note, BsonDocument>
                            {
                                OutputOptions = MapReduceOutputOptions.Replace("dryRunMapReduceTarget", databaseName: null)
                            }));
#pragma warning restore CS0618
                    }))
            ];

            // Action.
            var migrationOp = await migrationsDbContext.TryStartMigrationAsync(dryRun: true);
            Assert.NotNull(migrationOp);
            await migrationsDbContext.ExecuteMigrationAsync(migrationOp.Id);

            // Assert.
            //both guards fired client side, reporting the denied operation
            var aggregateGuardException = Assert.IsType<InvalidOperationException>(aggregateException);
            Assert.Equal("Aggregate to collection can't be simulated by a dry run", aggregateGuardException.Message);
            var mapReduceGuardException = Assert.IsType<InvalidOperationException>(mapReduceException);
            Assert.Equal("Map reduce with an output collection can't be simulated by a dry run", mapReduceGuardException.Message);

            //no output collection was created on the server
            await migrationsDbContext.Notes.AccessToCollectionAsync(async collection =>
            {
                var collectionNames = await (await collection.Database.ListCollectionNamesAsync()).ToListAsync();
                Assert.DoesNotContain("dryRunAggregateTarget", collectionNames);
                Assert.DoesNotContain("dryRunMapReduceTarget", collectionNames);
            });
        }

        [Fact]
        public async Task DryRunMigrationExecutesCustomProcessorWithoutPersisting()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await migrationsDbContext.Digests.DeleteManyAsync(Builders<Digest>.Filter.Empty);
            await migrationsDbContext.Notes.DeleteManyAsync(Builders<Note>.Filter.Empty);
            fixture.TaskRunner.ClearPending();

            var firstNote = new Note("first") { Tag = "original" };
            var secondNote = new Note("second");
            await migrationsDbContext.Notes.CreateAsync(firstNote);
            await migrationsDbContext.Notes.CreateAsync(secondNote);

            //a digest denormalizes the tag of the first note into its summary
            var digest = new Digest("digest", firstNote);
            await migrationsDbContext.Digests.CreateAsync(digest);

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

            //no dependencies update task is enqueued, and the denormalized summary stays untouched
            /* The dry run scope suppresses the propagation of the simulated note saves, and the
             * operation saves involve no reference member: nothing is left to enqueue. */
            Assert.Equal(0, fixture.TaskRunner.PendingCount);
            Assert.Equal("original", (await ReadRawDigestAsync(digest.Id))["PinnedNote"]["Tag"].AsString);

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

            var brokenDocument = await InsertBrokenNoteDocumentAsync("valid text");

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
            Assert.Equal(1, documentLog.TotMigratedDocs);
            Assert.Equal(1, documentLog.TotErrorDocs);
            var documentError = Assert.Single(documentLog.Errors);
            Assert.Equal(brokenDocument["_id"].ToString(), documentError.DocumentId);
            Assert.NotEmpty(documentError.Message);
        }

        [Fact]
        public async Task MigrationContinuesPastFailingDocuments()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await migrationsDbContext.Notes.DeleteManyAsync(Builders<Note>.Filter.Empty);

            /* The failing document sits between two valid ones: migrating both proves the
             * scan went on past it, whatever order the collection returns its documents. */
            var firstNote = new Note("first");
            await migrationsDbContext.Notes.CreateAsync(firstNote);
            var brokenDocument = await InsertBrokenNoteDocumentAsync("first");
            var secondNote = new Note("second");
            await migrationsDbContext.Notes.CreateAsync(secondNote);

            migrationsDbContext.DocumentMigrations =
            [
                new DocumentMigration<Note, string>(migrationsDbContext.Notes, async note =>
                {
                    note.Tag = "migrated";
                    await migrationsDbContext.SaveChangesAsync();
                })
            ];

            // Action.
            var migrationOp = await migrationsDbContext.TryStartMigrationAsync();
            Assert.NotNull(migrationOp);
            Assert.False(migrationOp.IsStopAtFirstErrorEnabled);
            await migrationsDbContext.ExecuteMigrationAsync(migrationOp.Id);

            // Assert.
            //every valid document migrated, and the failing one keeps its content
            using var verifyScope = fixture.ServiceProvider.CreateScope();
            var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<IMigrationsDbContext>();
            Assert.Equal("migrated", (await verifyDbContext.Notes.FindOneAsync(firstNote.Id)).Tag);
            Assert.Equal("migrated", (await verifyDbContext.Notes.FindOneAsync(secondNote.Id)).Tag);
            Assert.Contains(brokenDocument, await ListRawNoteDocumentsAsync());

            //the operation completed the scan, and closes failed reporting the failing document
            var completedOp = await verifyDbContext.GetMigrationAsync(migrationOp.Id);
            Assert.Equal(DbMigrationOperation.Status.Failed, completedOp.CurrentStatus);
            var documentLog = Assert.Single(completedOp.Logs.OfType<DocumentMigrationLog>());
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
            await migrationsDbContext.Digests.DeleteManyAsync(Builders<Digest>.Filter.Empty);
            await migrationsDbContext.Notes.DeleteManyAsync(Builders<Note>.Filter.Empty);
            fixture.TaskRunner.ClearPending();

            var note = new Note("first") { Tag = "original" };
            await migrationsDbContext.Notes.CreateAsync(note);

            //a digest denormalizes the tag of the note into its summary
            var digest = new Digest("digest", note);
            await migrationsDbContext.Digests.CreateAsync(digest);

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
            Assert.Equal("original", (await ReadRawDigestAsync(digest.Id))["PinnedNote"]["Tag"].AsString);

            await fixture.TaskRunner.ExecutePendingAsync(fixture.ServiceProvider);
            Assert.Equal("migrated", (await ReadRawDigestAsync(digest.Id))["PinnedNote"]["Tag"].AsString);
        }

        [Fact]
        public async Task MigrationReportsProgressOnSingleRollingLog()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await migrationsDbContext.Notes.DeleteManyAsync(Builders<Note>.Filter.Empty);

            //enough documents to raise a periodic progress report during the scan
            var note = new Note("template");
            await migrationsDbContext.Notes.CreateAsync(note);
            await InsertClonedNoteDocumentsAsync("template", 600);

            migrationsDbContext.DocumentMigrations =
                [new DocumentMigration<Note, string>(migrationsDbContext.Notes, _ => Task.CompletedTask)];

            // Action.
            var migrationOp = await migrationsDbContext.TryStartMigrationAsync();
            Assert.NotNull(migrationOp);
            await migrationsDbContext.ExecuteMigrationAsync(migrationOp.Id);

            // Assert.
            /* Progress reported through one rolling executing log, replaced by the ended one:
             * the operation document stays bounded regardless of the collection size. */
            using var verifyScope = fixture.ServiceProvider.CreateScope();
            var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<IMigrationsDbContext>();
            var completedOp = await verifyDbContext.GetMigrationAsync(migrationOp.Id);
            Assert.Equal(DbMigrationOperation.Status.Completed, completedOp.CurrentStatus);
            var documentLog = Assert.Single(completedOp.Logs.OfType<DocumentMigrationLog>());
            Assert.Equal(MigrationLogBase.ExecutionState.Succeded, documentLog.State);
            Assert.Equal(601, documentLog.TotMigratedDocs);
            Assert.Equal(0, documentLog.TotErrorDocs);
        }

        [Fact]
        public async Task MigrationScanDoesNotRetainReferencedSummaries()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await migrationsDbContext.Digests.DeleteManyAsync(Builders<Digest>.Filter.Empty);
            await migrationsDbContext.Notes.DeleteManyAsync(Builders<Note>.Filter.Empty);

            var note = new Note("text") { Tag = "original" };
            await migrationsDbContext.Notes.CreateAsync(note);

            //a digest denormalizes the tag of the note into its summary
            var digest = new Digest("digest", note);
            await migrationsDbContext.Digests.CreateAsync(digest);

            //the default migration replaces each scanned document as it is
            migrationsDbContext.DocumentMigrations =
                [new DocumentMigration<Digest, string>(migrationsDbContext.Digests)];

            // Action.
            var migrationOp = await migrationsDbContext.TryStartMigrationAsync();
            Assert.NotNull(migrationOp);
            await migrationsDbContext.ExecuteMigrationAsync(migrationOp.Id);

            // Assert.
            //neither the scanned digest nor its referenced note summary are retained on the scope
            Assert.Null(migrationsDbContext.TryGetLoadedModel(migrationsDbContext.Digests, digest.Id));
            Assert.Null(migrationsDbContext.TryGetLoadedModel(migrationsDbContext.Notes, note.Id));

            //the migration completed processing the digest, reading from a fresh scope
            using var verifyScope = fixture.ServiceProvider.CreateScope();
            var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<IMigrationsDbContext>();
            var completedOp = await verifyDbContext.GetMigrationAsync(migrationOp.Id);
            Assert.Equal(DbMigrationOperation.Status.Completed, completedOp.CurrentStatus);
            Assert.Contains(completedOp.Logs, log => log is DocumentMigrationLog
            {
                State: MigrationLogBase.ExecutionState.Succeded,
                TotMigratedDocs: 1,
                TotErrorDocs: 0
            });
        }

        [Fact]
        public async Task MigrationScanEvictsOnItsConfiguredInterval()
        {
            /* The eviction interval decides how many documents share the loaded state before it
             * evicts, and it is independent from the progress report interval. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await migrationsDbContext.Notes.DeleteManyAsync(Builders<Note>.Filter.Empty);

            List<string> noteIds = [];
            for (var i = 0; i < 4; i++)
            {
                var note = new Note($"note {i}");
                await migrationsDbContext.Notes.CreateAsync(note);
                noteIds.Add(note.Id);
            }
            noteIds.Sort(StringComparer.Ordinal); //the scan reads them by ascending id

            //at each document, how many of the notes scanned so far are still loaded
            List<int> loadedScannedNotesCounts = [];
            var docMigration = new DocumentMigration<Note, string>(
                migrationsDbContext.Notes,
                _ =>
                {
                    loadedScannedNotesCounts.Add(noteIds.Count(
                        id => migrationsDbContext.TryGetLoadedModel(migrationsDbContext.Notes, id) is not null));
                    return Task.CompletedTask;
                });

            // Action.
            var result = await docMigration.MigrateAsync(evictEveryTotDocuments: 2);

            // Assert.
            //the first two documents share the scope, the eviction after the second starts a new one
            Assert.True(result.Succeded);
            Assert.Equal([1, 2, 1, 2], loadedScannedNotesCounts);
            foreach (var noteId in noteIds)
                Assert.Null(migrationsDbContext.TryGetLoadedModel(migrationsDbContext.Notes, noteId));
        }

        [Fact]
        public async Task MigrationScanDoesNotRetainScannedModels()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await migrationsDbContext.Notes.DeleteManyAsync(Builders<Note>.Filter.Empty);

            List<string> noteIds = [];
            for (var i = 0; i < 5; i++)
            {
                var note = new Note($"note {i}");
                await migrationsDbContext.Notes.CreateAsync(note);
                noteIds.Add(note.Id);
            }

            List<Note> scannedNotes = [];
            migrationsDbContext.DocumentMigrations =
            [
                new DocumentMigration<Note, string>(migrationsDbContext.Notes, async n =>
                {
                    scannedNotes.Add(n);
                    n.Tag = "migrated";
                    await migrationsDbContext.SaveChangesAsync();
                })
            ];

            // Action.
            var migrationOp = await migrationsDbContext.TryStartMigrationAsync();
            Assert.NotNull(migrationOp);
            await migrationsDbContext.ExecuteMigrationAsync(migrationOp.Id);

            // Assert.
            //the scanned models left the scope state: loaded models, model documents and change candidates
            Assert.Equal(noteIds.Count, scannedNotes.Count);
            foreach (var scannedNote in scannedNotes)
            {
                Assert.Null(migrationsDbContext.TryGetLoadedModel(migrationsDbContext.Notes, scannedNote.Id));
                Assert.Null(migrationsDbContext.TryGetModelBsonDocument(scannedNote));
            }
            Assert.Empty(migrationsDbContext.ChangedModelsList);

            //every processed document persisted, and the operation completed, reading from a fresh scope
            using var verifyScope = fixture.ServiceProvider.CreateScope();
            var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<IMigrationsDbContext>();
            foreach (var noteId in noteIds)
                Assert.Equal("migrated", (await verifyDbContext.Notes.FindOneAsync(noteId)).Tag);
            var completedOp = await verifyDbContext.GetMigrationAsync(migrationOp.Id);
            Assert.Equal(DbMigrationOperation.Status.Completed, completedOp.CurrentStatus);
            Assert.Contains(completedOp.Logs, log => log is DocumentMigrationLog
            {
                State: MigrationLogBase.ExecutionState.Succeded,
                TotMigratedDocs: 5,
                TotErrorDocs: 0
            });
        }

        [Fact]
        public async Task MigrationScanKeepsModelsTrackedBeforeTheScan()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await migrationsDbContext.Digests.DeleteManyAsync(Builders<Digest>.Filter.Empty);
            await migrationsDbContext.Notes.DeleteManyAsync(Builders<Note>.Filter.Empty);

            var note = new Note("control") { Tag = "original" };
            await migrationsDbContext.Notes.CreateAsync(note);
            for (var i = 0; i < 4; i++)
                await migrationsDbContext.Digests.CreateAsync(new Digest($"digest {i}", note));

            //load the control note on the scope before the scan, like the migration operation is
            var loadedNote = await migrationsDbContext.Notes.FindOneAsync(note.Id);

            var docMigration = new DocumentMigration<Digest, string>(migrationsDbContext.Digests);

            // Action.
            //the callback runs between the per document evictions, like the operation updates do
            var callbackCount = 0;
            var result = await docMigration.MigrateAsync(2, async _ =>
            {
                callbackCount++;
                loadedNote.Tag = $"callback {callbackCount}";
                await migrationsDbContext.SaveChangesAsync();
            });

            // Assert.
            Assert.True(result.Succeded);
            Assert.Equal(4, result.MigratedDocuments);
            Assert.Equal(2, callbackCount);

            //the control note, entered before the scan, is still the loaded and tracked instance
            Assert.Same(loadedNote, migrationsDbContext.TryGetLoadedModel(migrationsDbContext.Notes, note.Id));
            Assert.NotNull(migrationsDbContext.TryGetModelBsonDocument(loadedNote));

            //the callback saves persisted through the scan evictions, reading from a fresh scope
            using var verifyScope = fixture.ServiceProvider.CreateScope();
            var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<IMigrationsDbContext>();
            Assert.Equal("callback 2", (await verifyDbContext.Notes.FindOneAsync(note.Id)).Tag);
        }

        [Fact]
        public async Task MigrationStopsAtFirstFailingDocumentWhenRequired()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await migrationsDbContext.Notes.DeleteManyAsync(Builders<Note>.Filter.Empty);

            await migrationsDbContext.Notes.CreateAsync(new Note("first"));
            await migrationsDbContext.Notes.CreateAsync(new Note("second"));
            await migrationsDbContext.Notes.CreateAsync(new Note("third"));

            //every document fails: only the first one processes, whatever order the scan follows
            var processedNotes = 0;
            migrationsDbContext.DocumentMigrations =
            [
                new DocumentMigration<Note, string>(migrationsDbContext.Notes, _ =>
                {
                    processedNotes++;
                    throw new InvalidOperationException("processor failure");
                })
            ];

            // Action.
            var migrationOp = await migrationsDbContext.TryStartMigrationAsync(stopAtFirstError: true);
            Assert.NotNull(migrationOp);
            Assert.True(migrationOp.IsStopAtFirstErrorEnabled);
            await migrationsDbContext.ExecuteMigrationAsync(migrationOp.Id);

            // Assert.
            Assert.Equal(1, processedNotes);

            //the operation reports the document aborting it, without scanning the others
            using var verifyScope = fixture.ServiceProvider.CreateScope();
            var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<IMigrationsDbContext>();
            var completedOp = await verifyDbContext.GetMigrationAsync(migrationOp.Id);
            Assert.True(completedOp.IsStopAtFirstErrorEnabled);
            Assert.Equal(DbMigrationOperation.Status.Failed, completedOp.CurrentStatus);
            var documentLog = Assert.Single(completedOp.Logs.OfType<DocumentMigrationLog>());
            Assert.Equal(MigrationLogBase.ExecutionState.Failed, documentLog.State);
            Assert.Equal(0, documentLog.TotMigratedDocs);
            Assert.Equal(1, documentLog.TotErrorDocs);
            var documentError = Assert.Single(documentLog.Errors);
            Assert.Contains("processor failure", documentError.Message, StringComparison.Ordinal);
        }

        // Helpers.
        /* Persist a raw note document breaking deserialization: a valid document, cloned with a
         * new id, gets a document value on its string text member. */
        private Task<BsonDocument> InsertBrokenNoteDocumentAsync(string sourceNoteText) =>
            migrationsDbContext.Notes.AccessToCollectionAsync(async collection =>
            {
                var rawCollection = collection.Database.GetCollection<BsonDocument>(
                    collection.CollectionNamespace.CollectionName);
                var documents = await (await rawCollection.FindAsync(FilterDefinition<BsonDocument>.Empty)).ToListAsync();
                var sourceDocument = documents.Single(d => d.Elements.Any(e => e.Value == sourceNoteText));

                var brokenDocument = (BsonDocument)sourceDocument.DeepClone();
                brokenDocument["_id"] = ObjectId.GenerateNewId();
                var textElementName = brokenDocument.Elements.Single(e => e.Value == sourceNoteText).Name;
                brokenDocument[textElementName] = new BsonDocument("broken", true);
                await rawCollection.InsertOneAsync(brokenDocument);

                return brokenDocument;
            });

        /* Persist raw copies of a valid note document, cloned with new ids: a fast way to
         * populate a large collection without a round trip per document. */
        private Task InsertClonedNoteDocumentsAsync(string sourceNoteText, int copies) =>
            migrationsDbContext.Notes.AccessToCollectionAsync(async collection =>
            {
                var rawCollection = collection.Database.GetCollection<BsonDocument>(
                    collection.CollectionNamespace.CollectionName);
                var documents = await (await rawCollection.FindAsync(FilterDefinition<BsonDocument>.Empty)).ToListAsync();
                var sourceDocument = documents.Single(d => d.Elements.Any(e => e.Value == sourceNoteText));

                var clonedDocuments = new List<BsonDocument>(copies);
                for (var i = 0; i < copies; i++)
                {
                    var clonedDocument = (BsonDocument)sourceDocument.DeepClone();
                    clonedDocument["_id"] = ObjectId.GenerateNewId();
                    clonedDocuments.Add(clonedDocument);
                }
                await rawCollection.InsertManyAsync(clonedDocuments);
            });

        private Task<List<BsonDocument>> ListRawNoteDocumentsAsync() =>
            migrationsDbContext.Notes.AccessToCollectionAsync(async collection =>
            {
                var rawCollection = collection.Database.GetCollection<BsonDocument>(
                    collection.CollectionNamespace.CollectionName);
                return await (await rawCollection.FindAsync(
                    FilterDefinition<BsonDocument>.Empty,
                    new FindOptions<BsonDocument> { Sort = Builders<BsonDocument>.Sort.Ascending("_id") })).ToListAsync();
            });

        private Task<BsonDocument> ReadRawDigestAsync(string digestId) =>
            migrationsDbContext.Digests.AccessToCollectionAsync(async collection =>
            {
                var rawCollection = collection.Database.GetCollection<BsonDocument>(
                    collection.CollectionNamespace.CollectionName);
                return await (await rawCollection.FindAsync(
                    Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(digestId)))).SingleAsync();
            });
    }
}
