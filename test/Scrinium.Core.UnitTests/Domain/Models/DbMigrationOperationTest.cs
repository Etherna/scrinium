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

using Etherna.Scrinium.Core.Domain.Models.DbMigrationOpAgg;
using Moq;
using System;
using System.Linq;
using Xunit;

namespace Etherna.Scrinium.Core.Domain.Models
{
    public class DbMigrationOperationTest
    {
        // Fields.
        private readonly DbMigrationOperation dbMigrationOp;

        // Constructor.
        public DbMigrationOperationTest()
        {
            var engineMock = new Mock<IDbContextEngine>();
            engineMock.Setup(e => e.Identifier).Returns("FakeDbContext");

            dbMigrationOp = new DbMigrationOperation(engineMock.Object);
        }

        // Tests.
        [Fact]
        public void AddLogAppendsWithoutReplacing()
        {
            // Setup.
            dbMigrationOp.AddLog(new DocumentMigrationLog(
                "models", MigrationLogBase.ExecutionState.Executing, 500));

            // Action.
            dbMigrationOp.AddLog(new DocumentMigrationLog(
                "models", MigrationLogBase.ExecutionState.Executing, 1000));

            // Assert.
            Assert.Equal(2, dbMigrationOp.Logs.Count());
        }

        [Fact]
        public void AddDocumentMigrationLogKeepsLogsOfOtherCollectionsAndTypes()
        {
            // Setup.
            dbMigrationOp.AddLog(new DeleteOldIndexesMigrationLog(
                "modelsA", MigrationLogBase.ExecutionState.Succeeded));
            dbMigrationOp.AddDocumentMigrationLog(new DocumentMigrationLog(
                "modelsA", MigrationLogBase.ExecutionState.Executing, 500));
            dbMigrationOp.AddDocumentMigrationLog(new DocumentMigrationLog(
                "modelsB", MigrationLogBase.ExecutionState.Executing, 100));

            // Action.
            dbMigrationOp.AddDocumentMigrationLog(new DocumentMigrationLog(
                "modelsA", MigrationLogBase.ExecutionState.Succeeded, 750));

            // Assert.
            //only the executing progress log of the same collection is replaced
            Assert.Contains(dbMigrationOp.Logs, log => log is DeleteOldIndexesMigrationLog);
            Assert.Contains(dbMigrationOp.Logs, log => log is DocumentMigrationLog
            {
                CollectionName: "modelsA",
                State: MigrationLogBase.ExecutionState.Succeeded,
                TotMigratedDocs: 750
            });
            Assert.Contains(dbMigrationOp.Logs, log => log is DocumentMigrationLog
            {
                CollectionName: "modelsB",
                State: MigrationLogBase.ExecutionState.Executing,
                TotMigratedDocs: 100
            });
            Assert.DoesNotContain(dbMigrationOp.Logs, log => log is DocumentMigrationLog
            {
                CollectionName: "modelsA",
                State: MigrationLogBase.ExecutionState.Executing
            });
        }

        [Fact]
        public void AddDocumentMigrationLogReplacesExecutingLogOfSameCollection()
        {
            // Setup.
            dbMigrationOp.AddDocumentMigrationLog(new DocumentMigrationLog(
                "models", MigrationLogBase.ExecutionState.Executing, 500));

            // Action.
            dbMigrationOp.AddDocumentMigrationLog(new DocumentMigrationLog(
                "models", MigrationLogBase.ExecutionState.Executing, 1000));

            // Assert.
            //progress reports as one rolling entry per collection, with its running counter
            var documentLog = Assert.IsType<DocumentMigrationLog>(Assert.Single(dbMigrationOp.Logs));
            Assert.Equal(MigrationLogBase.ExecutionState.Executing, documentLog.State);
            Assert.Equal(1000, documentLog.TotMigratedDocs);
        }

        [Fact]
        public void ObserveAbortRequestKeepsTheFirstRequestOnAnOpenOperation()
        {
            // Setup.
            var firstRequest = new DateTimeOffset(2026, 10, 20, 16, 00, 00, TimeSpan.Zero);
            dbMigrationOp.TaskStarted("task");

            // Action.
            dbMigrationOp.ObserveAbortRequest(firstRequest);
            dbMigrationOp.ObserveAbortRequest(firstRequest.AddMinutes(1));

            // Assert.
            //the first request is the one that counts, and a closed operation takes none
            Assert.Equal(firstRequest, dbMigrationOp.AbortRequestedDateTime);
            dbMigrationOp.TaskCancelled();
            Assert.Throws<InvalidOperationException>(() => dbMigrationOp.ObserveAbortRequest(firstRequest));
        }

        [Fact]
        public void TaskCancelledInterruptsTheExecutingLogs()
        {
            // Setup.
            dbMigrationOp.AddDocumentMigrationLog(new DocumentMigrationLog(
                "models", MigrationLogBase.ExecutionState.Executing, 500));

            // Action.
            dbMigrationOp.TaskCancelled();

            // Assert.
            var documentLog = Assert.IsType<DocumentMigrationLog>(Assert.Single(dbMigrationOp.Logs));
            Assert.Equal(MigrationLogBase.ExecutionState.Interrupted, documentLog.State);
            Assert.Equal(500, documentLog.TotMigratedDocs);
        }

        [Fact]
        public void TaskCancelledStampsTheClosingInstant()
        {
            // Action.
            dbMigrationOp.TaskCancelled();

            // Assert.
            //the closing instant belongs to every terminal status, not to the completed one alone
            Assert.Equal(DbMigrationOperation.Status.Cancelled, dbMigrationOp.CurrentStatus);
            Assert.NotNull(dbMigrationOp.CompletedDateTime);
            Assert.Null(dbMigrationOp.FailureReason);
        }

        [Fact]
        public void TaskFailedInterruptsTheExecutingLogsKeepingTheirCounters()
        {
            /* The incident this guards: an operation closed failed with its rolling progress
             * log still executing, reading as a scan in progress under a failed badge, with no
             * way to tell how far it got. */

            // Setup.
            dbMigrationOp.TaskStarted("task");
            dbMigrationOp.AddLog(new DeleteOldIndexesMigrationLog(
                "modelsA", MigrationLogBase.ExecutionState.Succeeded));
            dbMigrationOp.AddDocumentMigrationLog(new DocumentMigrationLog(
                "modelsA",
                MigrationLogBase.ExecutionState.Executing,
                207629500,
                [new DocumentMigrationError("doc1", "FormatException: bad value")],
                1));
            dbMigrationOp.AddLog(new BuildNewIndexesMigrationLog(
                "modelsB", MigrationLogBase.ExecutionState.Executing));

            // Action.
            dbMigrationOp.TaskFailed("TimeoutException: the cursor timed out");

            // Assert.
            var logs = dbMigrationOp.Logs.ToArray();
            Assert.Equal(3, logs.Length);
            //the ended logs stay as they are
            Assert.Equal(MigrationLogBase.ExecutionState.Succeeded, logs[0].State);
            //the executing ones close interrupted, with what they reported until the stop
            var documentLog = Assert.IsType<DocumentMigrationLog>(logs[1]);
            Assert.Equal(MigrationLogBase.ExecutionState.Interrupted, documentLog.State);
            Assert.Equal("modelsA", documentLog.CollectionName);
            Assert.Equal(207629500, documentLog.TotMigratedDocs);
            Assert.Equal(1, documentLog.TotErrorDocs);
            Assert.Single(documentLog.Errors);
            var buildLog = Assert.IsType<BuildNewIndexesMigrationLog>(logs[2]);
            Assert.Equal(MigrationLogBase.ExecutionState.Interrupted, buildLog.State);
            Assert.Equal("modelsB", buildLog.Repository);
        }

        [Fact]
        public void TaskFailedRecordsTheReasonAndTheClosingInstant()
        {
            // Setup.
            dbMigrationOp.TaskStarted("task");

            // Action.
            dbMigrationOp.TaskFailed("TimeoutException: the cursor timed out");

            // Assert.
            Assert.Equal(DbMigrationOperation.Status.Failed, dbMigrationOp.CurrentStatus);
            Assert.Equal("TimeoutException: the cursor timed out", dbMigrationOp.FailureReason);
            Assert.NotNull(dbMigrationOp.CompletedDateTime);
        }

        [Fact]
        public void TaskStartedDeniesARunningOperationNamingItsStatus()
        {
            /* The task runner re-executing the job of a dead worker is the caller meeting this
             * denial: what it records has to say which operation, and why. */

            // Setup.
            dbMigrationOp.TaskStarted("task");

            // Action.
            var exception = Assert.Throws<InvalidOperationException>(() => dbMigrationOp.TaskStarted("retry"));

            // Assert.
            Assert.Contains("start the migration operation", exception.Message, StringComparison.Ordinal);
            Assert.Contains("status is Running", exception.Message, StringComparison.Ordinal);
            Assert.Equal("task", dbMigrationOp.TaskId);
        }
    }
}
