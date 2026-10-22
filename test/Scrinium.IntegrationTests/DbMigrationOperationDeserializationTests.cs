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
using Etherna.MongoDB.Bson.IO;
using Etherna.MongoDB.Bson.Serialization;
using Etherna.MongoDB.Driver;
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Domain.Models.DbMigrationOpAgg;
using Etherna.Scrinium.Core.Domain.Models.ReferencesRepairOpAgg;
using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Etherna.Scrinium.Core.Options;
using Etherna.Scrinium.Core.Serialization.Serializers;
using Etherna.Scrinium.Core.Utility;
using Etherna.Scrinium.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.IntegrationTests
{
    /* The operations collection of a long lived application carries the documents that every
     * version of the library wrote into it, and they all deserialize through the internal model
     * maps of the engine. The source documents below are listed from the newest version down,
     * each one commented with its schema id and the version that wrote it. */
    [Collection("Integration")]
    public class DbMigrationOperationDeserializationTests : IDisposable
    {
        // Fields.
        private readonly IMigrationsDbContext dbContext;
        private readonly IServiceScope scope;

        // Constructor.
        public DbMigrationOperationDeserializationTests(IntegrationFixture fixture)
        {
            ArgumentNullException.ThrowIfNull(fixture);

            scope = fixture.ServiceProvider.CreateScope();
            dbContext = scope.ServiceProvider.GetRequiredService<IMigrationsDbContext>();
        }

        // Dispose.
        public void Dispose()
        {
            scope.Dispose();
            GC.SuppressFinalize(this);
        }

        // Tests.
        // "9576a398-3a70-42e1-9beb-491df95bde19" - 0.25.8, succeeded state on its current spelling
        [Fact]
        public void DeserializeOperationWrittenBy0258()
        {
            // Setup.
            const string sourceDocument =
                """
                {
                    "_id" : ObjectId("62c0f7e5a0b1c2d3e4f50004"),
                    "_s" : "9576a398-3a70-42e1-9beb-491df95bde19",
                    "_t" : "DbMigrationOperation",
                    "DbContextName" : "MigrationsDbContext",
                    "CompletedDateTime" : ISODate("2026-10-20T15:22:52.365+0000"),
                    "CurrentStatus" : "Completed",
                    "IsDeprecatedSchemaRewriteEnabled" : false,
                    "IsDryRun" : true,
                    "IsStopAtFirstErrorEnabled" : false,
                    "Logs" : [
                        {
                            "_s" : "ac9d8011-6247-4365-b8ca-ac8401f838a1",
                            "_t" : "DeleteOldIndexesMigrationLog",
                            "State" : "Succeeded",
                            "CreationDateTime" : ISODate("2026-10-20T15:22:52.364+0000"),
                            "Repository" : "notes"
                        },
                        {
                            "_s" : "d2b49514-464e-4b28-8b38-ad2d0cc69d3e",
                            "_t" : "DocumentMigrationLog",
                            "State" : "Succeeded",
                            "CreationDateTime" : ISODate("2026-10-20T15:22:52.364+0000"),
                            "CollectionName" : "notes",
                            "Errors" : [],
                            "TotErrorDocs" : NumberLong(0),
                            "TotMigratedDocs" : NumberLong(42)
                        }
                    ],
                    "TaskId" : "task-1"
                }
                """;

            // Action.
            var operation = DeserializeOperation<DbMigrationOperation>(sourceDocument);

            // Assert.
            Assert.Equal("62c0f7e5a0b1c2d3e4f50004", operation.Id);
            Assert.Equal(DbMigrationOperation.Status.Completed, operation.CurrentStatus);
            Assert.False(operation.IsDeprecatedSchemaRewriteEnabled);
            Assert.True(operation.IsDryRun);

            var logs = operation.Logs.ToArray();
            Assert.Equal(2, logs.Length);
            var deleteLog = Assert.IsType<DeleteOldIndexesMigrationLog>(logs[0]);
            Assert.Equal("notes", deleteLog.Repository);
            Assert.Equal(MigrationLogBase.ExecutionState.Succeeded, deleteLog.State);
            var documentLog = Assert.IsType<DocumentMigrationLog>(logs[1]);
            Assert.Equal("notes", documentLog.CollectionName);
            Assert.Equal(MigrationLogBase.ExecutionState.Succeeded, documentLog.State);
            Assert.Equal(42, documentLog.TotMigratedDocs);
        }

        // "9576a398-3a70-42e1-9beb-491df95bde19" - 0.25.8, failed with its reason and closing instant
        [Fact]
        public void DeserializeFailedOperationWrittenBy0258()
        {
            // Setup.
            const string sourceDocument =
                """
                {
                    "_id" : ObjectId("62c0f7e5a0b1c2d3e4f50006"),
                    "_s" : "9576a398-3a70-42e1-9beb-491df95bde19",
                    "_t" : "DbMigrationOperation",
                    "DbContextName" : "MigrationsDbContext",
                    "CompletedDateTime" : ISODate("2026-10-20T01:37:17.000+0000"),
                    "CurrentStatus" : "Failed",
                    "FailureReason" : "Documents migration failed on \"chunks\" repository: OperationCanceledException: The operation was canceled.",
                    "IsDeprecatedSchemaRewriteEnabled" : true,
                    "IsDryRun" : true,
                    "IsStopAtFirstErrorEnabled" : true,
                    "Logs" : [
                        {
                            "_s" : "d2b49514-464e-4b28-8b38-ad2d0cc69d3e",
                            "_t" : "DocumentMigrationLog",
                            "State" : "Failed",
                            "CreationDateTime" : ISODate("2026-10-20T01:37:17.000+0000"),
                            "CollectionName" : "chunks",
                            "Errors" : [],
                            "TotErrorDocs" : NumberLong(0),
                            "TotMigratedDocs" : NumberLong(207629500)
                        }
                    ],
                    "TaskId" : "6ad6247db54d54243003b014"
                }
                """;

            // Action.
            var operation = DeserializeOperation<DbMigrationOperation>(sourceDocument);

            // Assert.
            Assert.Equal("62c0f7e5a0b1c2d3e4f50006", operation.Id);
            Assert.Equal(new DateTimeOffset(2026, 10, 20, 01, 37, 17, TimeSpan.Zero), operation.CompletedDateTime);
            Assert.Equal(DbMigrationOperation.Status.Failed, operation.CurrentStatus);
            Assert.Equal(
                "Documents migration failed on \"chunks\" repository: OperationCanceledException: The operation was canceled.",
                operation.FailureReason);
            Assert.True(operation.IsDeprecatedSchemaRewriteEnabled);

            var documentLog = Assert.IsType<DocumentMigrationLog>(Assert.Single(operation.Logs));
            Assert.Equal(MigrationLogBase.ExecutionState.Failed, documentLog.State);
            Assert.Equal(207629500, documentLog.TotMigratedDocs);
        }

        // "afdb63c9-791b-41f8-8216-556e233df0de" - 0.25.0, the schema deprecated on 0.25.8: legacy spelling of the succeeded state
        [Fact]
        public void DeserializeOperationWrittenBy0250()
        {
            // Setup.
            const string sourceDocument =
                """
                {
                    "_id" : ObjectId("62c0f7e5a0b1c2d3e4f50002"),
                    "_s" : "afdb63c9-791b-41f8-8216-556e233df0de",
                    "_t" : "DbMigrationOperation",
                    "DbContextName" : "MigrationsDbContext",
                    "CompletedDateTime" : ISODate("2026-09-01T10:05:00.000+0000"),
                    "CurrentStatus" : "Completed",
                    "IsDryRun" : true,
                    "IsStopAtFirstErrorEnabled" : true,
                    "Logs" : [
                        {
                            "_s" : "d2b49514-464e-4b28-8b38-ad2d0cc69d3e",
                            "_t" : "DocumentMigrationLog",
                            "State" : "Succeded",
                            "CreationDateTime" : ISODate("2026-09-01T10:01:00.000+0000"),
                            "CollectionName" : "notes",
                            "Errors" : [
                                {
                                    "_s" : "15b8f6c8-8e94-4849-a3ce-4f0eb2cbb556",
                                    "DocumentId" : "62c0f7e5a0b1c2d3e4f50003",
                                    "Message" : "Unknown schema id"
                                }
                            ],
                            "TotErrorDocs" : NumberLong(1),
                            "TotMigratedDocs" : NumberLong(42)
                        }
                    ],
                    "TaskId" : "1f77c0ea"
                }
                """;

            // Action.
            var operation = DeserializeOperation<DbMigrationOperation>(sourceDocument);

            // Assert.
            Assert.Equal("62c0f7e5a0b1c2d3e4f50002", operation.Id);
            Assert.Equal(new DateTimeOffset(2026, 09, 01, 10, 05, 00, TimeSpan.Zero), operation.CompletedDateTime);
            Assert.Equal(DbMigrationOperation.Status.Completed, operation.CurrentStatus);
            Assert.Equal("MigrationsDbContext", operation.DbContextName);
            Assert.True(operation.IsDryRun);
            Assert.True(operation.IsStopAtFirstErrorEnabled);
            Assert.Equal("1f77c0ea", operation.TaskId);

            var documentLog = Assert.IsType<DocumentMigrationLog>(Assert.Single(operation.Logs));
            Assert.Equal("notes", documentLog.CollectionName);
            Assert.Equal(new DateTimeOffset(2026, 09, 01, 10, 01, 00, TimeSpan.Zero), documentLog.CreationDateTime);
            Assert.Equal(MigrationLogBase.ExecutionState.Succeeded, documentLog.State);
            Assert.Equal(1, documentLog.TotErrorDocs);
            Assert.Equal(42, documentLog.TotMigratedDocs);
            var error = Assert.Single(documentLog.Errors);
            Assert.Equal("62c0f7e5a0b1c2d3e4f50003", error.DocumentId);
            Assert.Equal("Unknown schema id", error.Message);
        }

        // "afdb63c9-791b-41f8-8216-556e233df0de" - MongODM 0.24, schema id on the deprecated element
        [Fact]
        public void DeserializeOperationWrittenByMongodm024()
        {
            // Setup.
            const string sourceDocument =
                """
                {
                    "_id" : ObjectId("62c0f7e5a0b1c2d3e4f50001"),
                    "_m" : "afdb63c9-791b-41f8-8216-556e233df0de",
                    "_t" : "DbMigrationOperation",
                    "CreationDateTime" : ISODate("2024-06-01T10:00:00.000+0000"),
                    "DbContextName" : "MigrationsDbContext",
                    "CompletedDateTime" : ISODate("2024-06-01T10:05:00.000+0000"),
                    "CurrentStatus" : "Completed",
                    "Logs" : [
                        {
                            "_m" : "d2b49514-464e-4b28-8b38-ad2d0cc69d3e",
                            "_t" : "DocumentMigrationLog",
                            "State" : "Succeded",
                            "CreationDateTime" : ISODate("2024-06-01T10:01:00.000+0000"),
                            "CollectionName" : "notes",
                            "TotMigratedDocs" : NumberLong(42)
                        },
                        {
                            "_m" : "24d65670-a3c3-443c-977a-51112df04e2a",
                            "_t" : "IndexMigrationLog",
                            "State" : "Succeded",
                            "CreationDateTime" : ISODate("2024-06-01T10:02:00.000+0000"),
                            "Repository" : "notes"
                        }
                    ],
                    "TaskId" : "9d2ad0b6"
                }
                """;

            // Action.
            var operation = DeserializeOperation<DbMigrationOperation>(sourceDocument);

            // Assert.
            Assert.Equal("62c0f7e5a0b1c2d3e4f50001", operation.Id);
            Assert.Equal(new DateTimeOffset(2024, 06, 01, 10, 05, 00, TimeSpan.Zero), operation.CompletedDateTime);
            Assert.Equal(DbMigrationOperation.Status.Completed, operation.CurrentStatus);
            Assert.Equal("MigrationsDbContext", operation.DbContextName);
            Assert.False(operation.IsDryRun);
            Assert.False(operation.IsStopAtFirstErrorEnabled);
            Assert.Equal("9d2ad0b6", operation.TaskId);

            var logs = operation.Logs.ToArray();
            Assert.Equal(2, logs.Length);

            //the migration errors didn't exist yet: the log reads with none of them
            var documentLog = Assert.IsType<DocumentMigrationLog>(logs[0]);
            Assert.Equal("notes", documentLog.CollectionName);
            Assert.Equal(new DateTimeOffset(2024, 06, 01, 10, 01, 00, TimeSpan.Zero), documentLog.CreationDateTime);
            Assert.Equal(MigrationLogBase.ExecutionState.Succeeded, documentLog.State);
            Assert.Empty(documentLog.Errors);
            Assert.Equal(0, documentLog.TotErrorDocs);
            Assert.Equal(42, documentLog.TotMigratedDocs);

#pragma warning disable CS0618 // Type or member is obsolete
            var indexLog = Assert.IsType<IndexMigrationLog>(logs[1]);
            Assert.Equal("notes", indexLog.Repository);
#pragma warning restore CS0618 // Type or member is obsolete
            Assert.Equal(new DateTimeOffset(2024, 06, 01, 10, 02, 00, TimeSpan.Zero), indexLog.CreationDateTime);
            Assert.Equal(MigrationLogBase.ExecutionState.Succeeded, indexLog.State);
        }

        // "f2a7c1e4-63b9-4d08-9a35-7e0c4b8d1f62" - 0.25.7, the schema deprecated on 0.25.8: legacy spelling of the succeeded state
        [Fact]
        public void DeserializeReferencesRepairOperationWrittenBy0257()
        {
            // Setup.
            const string sourceDocument =
                """
                {
                    "_id" : ObjectId("62c0f7e5a0b1c2d3e4f50005"),
                    "_s" : "f2a7c1e4-63b9-4d08-9a35-7e0c4b8d1f62",
                    "_t" : "ReferencesRepairOperation",
                    "DbContextName" : "MigrationsDbContext",
                    "CompletedDateTime" : ISODate("2026-10-19T08:30:00.000+0000"),
                    "CurrentStatus" : "Completed",
                    "IsDryRun" : false,
                    "PathStates" : [
                        {
                            "_s" : "6d3e0b95-84f1-42a7-b0c6-9f5a2d7e3b18",
                            "DeletedDocumentsCount" : NumberLong(0),
                            "ElementPath" : "Note",
                            "ErrorMessage" : null,
                            "MissingOriginIdsCount" : NumberLong(3),
                            "RepairMode" : "RemoveReference",
                            "State" : "Succeded",
                            "UpdatedDocumentsCount" : NumberLong(2)
                        }
                    ],
                    "RepositoryName" : "digests",
                    "TaskId" : "task-2"
                }
                """;

            // Action.
            var operation = DeserializeOperation<ReferencesRepairOperation>(sourceDocument);

            // Assert.
            Assert.Equal("62c0f7e5a0b1c2d3e4f50005", operation.Id);
            Assert.Equal(ReferencesRepairOperation.Status.Completed, operation.CurrentStatus);
            Assert.Equal("digests", operation.RepositoryName);

            var pathState = Assert.Single(operation.PathStates);
            Assert.Equal("Note", pathState.ElementPath);
            Assert.Equal(OriginDeleteMode.RemoveReference, pathState.RepairMode);
            Assert.Equal(ReferencesRepairPathState.ExecutionState.Succeeded, pathState.State);
            Assert.Equal(3, pathState.MissingOriginIdsCount);
            Assert.Equal(2, pathState.UpdatedDocumentsCount);
        }

        [Fact]
        public async Task StoredOperationsCarryTheCurrentSpellingOfTheSucceededState()
        {
            /* The legacy spelling is read, never written: what the operations write from now
             * on carries the current name, on the migration logs and on the repair path
             * states alike, under the active schema of the operation — the one the rewrite of
             * the deprecated schemas leaves alone. */

            // Setup.
            var migrationOperation = new DbMigrationOperation(dbContext.Engine, isDryRun: true);
            migrationOperation.TaskStarted("task-1");
            migrationOperation.AddDocumentMigrationLog(new DocumentMigrationLog(
                "notes", MigrationLogBase.ExecutionState.Succeeded, 42));
            migrationOperation.TaskCompleted();

            var repairOperation = new ReferencesRepairOperation(
                dbContext.Engine,
                "digests",
                new Dictionary<string, OriginDeleteMode> { ["Note"] = OriginDeleteMode.RemoveReference });
            repairOperation.TaskStarted("task-2");
            repairOperation.ReportPathsStarted();
            repairOperation.ReportPathEnded("Note", 3, 2, 0);
            repairOperation.TaskCompleted();

            try
            {
                // Action.
                await dbContext.DbOperations.CreateAsync(migrationOperation);
                await dbContext.DbOperations.CreateAsync(repairOperation);

                // Assert.
                var migrationDocument = await ReadStoredDocumentAsync(migrationOperation.Id);
                Assert.Equal(ActiveSchemaId(typeof(DbMigrationOperation)), migrationDocument["_s"].AsString);
                Assert.Equal("Succeeded", migrationDocument["Logs"][0]["State"].AsString);
                var repairDocument = await ReadStoredDocumentAsync(repairOperation.Id);
                Assert.Equal(ActiveSchemaId(typeof(ReferencesRepairOperation)), repairDocument["_s"].AsString);
                Assert.Equal("Succeeded", repairDocument["PathStates"][0]["State"].AsString);
            }
            finally
            {
                //the operations history of the db context stays as the other tests expect it
                await dbContext.DbOperations.DeleteAsync(migrationOperation);
                await dbContext.DbOperations.DeleteAsync(repairOperation);
            }
        }

        // Helpers.
        private string ActiveSchemaId(Type operationType) =>
            dbContext.Engine.MapRegistry.GetActiveSchemaIdBsonElement(operationType).Value.AsString;

        private TOperation DeserializeOperation<TOperation>(string sourceDocument)
            where TOperation : OperationBase
        {
            using var documentReader = new JsonReader(sourceDocument);
            var modelMapSerializer = new ModelMapSerializer<TOperation>(dbContext.Engine);

            using var asyncLocalContext = AsyncLocalContext.Instance.InitAsyncLocalContext();
            //read on the repository owning the operations, as the migration state reads do
            using var dbExecutionContext = new DbExecutionContextHandler(dbContext, dbContext.DbOperations);
            return modelMapSerializer.Deserialize(BsonDeserializationContext.CreateRoot(documentReader));
        }

        //the document as the collection stores it, read raw: the serializer under test wrote it
        private Task<BsonDocument> ReadStoredDocumentAsync(string operationId) =>
            dbContext.DbOperations.AccessToCollectionAsync(collection =>
                collection.Find(Builders<OperationBase>.Filter.Eq("_id", new ObjectId(operationId)))
                    .As<BsonDocument>()
                    .SingleAsync());
    }
}
