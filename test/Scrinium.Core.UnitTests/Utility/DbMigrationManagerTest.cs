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

using Etherna.MongoDB.Driver;
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Domain.Models.DbMigrationOpAgg;
using Etherna.Scrinium.Core.Exceptions;
using Etherna.Scrinium.Core.ExecContext;
using Etherna.Scrinium.Core.Migration;
using Etherna.Scrinium.Core.Options;
using Etherna.Scrinium.Core.Repositories;
using Etherna.Scrinium.Core.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.Core.Utility
{
    public class DbMigrationManagerTest
    {
        // Fields.
        private readonly DbMigrationManager dbMigrationManager;
        private readonly DbMigrationOperation dbMigrationOp;

        private readonly Mock<IResourceLockLease> dbContextLockLeaseMock = new();
        private readonly Mock<IResourceLock> dbContextLockMock = new();
        private readonly Mock<IDbContext> dbContextMock = new();
        private readonly Mock<IRepository<OperationBase, string>> dbOperationsMock = new();
        private readonly Mock<IDbContextEngine> engineMock = new();
        private readonly Mock<IExecutionContext> executionContextMock = new();
        private readonly Mock<IDbContextOptions> optionsMock = new();
        private readonly Mock<IRepositoryRegistry> repositoryRegistryMock = new();
        private readonly Mock<ITaskRunner> taskRunnerMock = new();

        // Constructor.
        public DbMigrationManagerTest()
        {
            executionContextMock.Setup(c => c.Items).Returns(new Dictionary<object, object?>());
            optionsMock.Setup(o => o.DbName).Returns("test-db");
            dbContextLockMock.Setup(l => l.TryClaimAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
                .ReturnsAsync(true);
            dbContextLockMock.Setup(l => l.TryResumeClaimAsync(It.IsAny<string>()))
                .ReturnsAsync(dbContextLockLeaseMock.Object);
            engineMock.Setup(e => e.DbContextLock).Returns(dbContextLockMock.Object);
            engineMock.Setup(e => e.ExecutionContext).Returns(executionContextMock.Object);
            engineMock.Setup(e => e.Identifier).Returns("FakeDbContext");
            engineMock.Setup(e => e.Options).Returns(optionsMock.Object);

            dbMigrationOp = new DbMigrationOperation(engineMock.Object);
            dbOperationsMock.Setup(r => r.FindOneAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(dbMigrationOp);
            dbOperationsMock.Setup(r => r.UpdateManyAsync(
                    It.IsAny<FilterDefinition<OperationBase>>(),
                    It.IsAny<UpdateDefinition<OperationBase>>(),
                    It.IsAny<UpdateOptions?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UpdateResult.Acknowledged(0, 0, null));
            repositoryRegistryMock.Setup(r => r.Repositories).Returns([]);

            dbContextMock.Setup(c => c.DbOperations).Returns(dbOperationsMock.Object);
            dbContextMock.Setup(c => c.DocumentMigrationList).Returns([]);
            dbContextMock.Setup(c => c.Engine).Returns(engineMock.Object);
            dbContextMock.Setup(c => c.RepositoryRegistry).Returns(repositoryRegistryMock.Object);

            dbMigrationManager = new DbMigrationManager(taskRunnerMock.Object);
            dbMigrationManager.Initialize(engineMock.Object, new Mock<ILogger>().Object);
        }

        // Tests.
        [Fact]
        public async Task ExecuteDryRunMigrationReportsDocumentErrors()
        {
            // Setup.
            var dryRunOp = new DbMigrationOperation(engineMock.Object, isDryRun: true);
            dbOperationsMock.Setup(r => r.FindOneAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(dryRunOp);

            var repositoryMock = new Mock<IRepository>();
            repositoryMock.Setup(r => r.Name).Returns("fakeModels");
            var docMigrationMock = new Mock<DocumentMigration>();
            docMigrationMock.Setup(m => m.SourceRepository).Returns(repositoryMock.Object);
            docMigrationMock.Setup(m => m.MigrateAsync(It.IsAny<int>(), It.IsAny<Func<long, Task>?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(MigrationResult.Failed(
                    10,
                    documentErrors: [new DocumentMigrationError("doc1", "FormatException: bad value")],
                    totDocumentErrors: 2));
            dbContextMock.Setup(c => c.DocumentMigrationList).Returns([docMigrationMock.Object]);

            // Action.
            var migrationException = await Assert.ThrowsAsync<ScriniumDbMigrationException>(() =>
                dbMigrationManager.ExecuteDbContextMigrationAsync(dbContextMock.Object, "opId", throwOnErrors: true));

            // Assert.
            Assert.Equal(DbMigrationOperation.Status.Failed, dryRunOp.CurrentStatus);
            var aggregateException = Assert.IsType<AggregateException>(migrationException.InnerException);
            Assert.Contains("2 document errors", Assert.Single(aggregateException.InnerExceptions).Message, StringComparison.Ordinal);
            var documentLog = Assert.IsType<DocumentMigrationLog>(Assert.Single(dryRunOp.Logs));
            Assert.Equal(MigrationLogBase.ExecutionState.Failed, documentLog.State);
            Assert.Equal(10, documentLog.TotMigratedDocs);
            Assert.Equal(2, documentLog.TotErrorDocs);
            var documentError = Assert.Single(documentLog.Errors);
            Assert.Equal("doc1", documentError.DocumentId);
            Assert.Equal("FormatException: bad value", documentError.Message);
        }

        [Fact]
        public async Task ExecuteDryRunMigrationSkipsIndexSteps()
        {
            // Setup.
            var dryRunOp = new DbMigrationOperation(engineMock.Object, isDryRun: true);
            dbOperationsMock.Setup(r => r.FindOneAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(dryRunOp);

            var repositoryMock = new Mock<IRepository>();
            repositoryMock.Setup(r => r.Name).Returns("fakeModels");
            repositoryRegistryMock.Setup(r => r.Repositories).Returns([repositoryMock.Object]);
            var docMigrationMock = new Mock<DocumentMigration>();
            docMigrationMock.Setup(m => m.SourceRepository).Returns(repositoryMock.Object);
            docMigrationMock.Setup(m => m.MigrateAsync(It.IsAny<int>(), It.IsAny<Func<long, Task>?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(MigrationResult.Succeeded(3));
            dbContextMock.Setup(c => c.DocumentMigrationList).Returns([docMigrationMock.Object]);

            // Action.
            await dbMigrationManager.ExecuteDbContextMigrationAsync(dbContextMock.Object, "opId");

            // Assert.
            Assert.Equal(DbMigrationOperation.Status.Completed, dryRunOp.CurrentStatus);
            repositoryMock.Verify(r => r.DeleteOldIndexesAsync(It.IsAny<CancellationToken>()), Times.Never());
            repositoryMock.Verify(r => r.BuildNewIndexesAsync(It.IsAny<CancellationToken>()), Times.Never());
            docMigrationMock.Verify(m => m.MigrateAsync(It.IsAny<int>(), It.IsAny<Func<long, Task>?>(), true, false, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once());
            Assert.DoesNotContain(dryRunOp.Logs, log => log is DeleteOldIndexesMigrationLog or BuildNewIndexesMigrationLog);
            Assert.Contains(dryRunOp.Logs, log => log is DocumentMigrationLog { State: MigrationLogBase.ExecutionState.Succeded, TotMigratedDocs: 3 });
        }

        [Fact]
        public async Task ExecuteMigrationCancelsOperationWithoutLockClaim()
        {
            // Setup.
            //the lock has been taken over by another owner, or released: the claim can't resume
            dbContextLockMock.Setup(l => l.TryResumeClaimAsync(It.IsAny<string>()))
                .ReturnsAsync((IResourceLockLease?)null);

            var docMigrationMock = new Mock<DocumentMigration>();
            dbContextMock.Setup(c => c.DocumentMigrationList).Returns([docMigrationMock.Object]);

            // Action.
            var migrationException = await Assert.ThrowsAsync<ScriniumDbMigrationException>(() =>
                dbMigrationManager.ExecuteDbContextMigrationAsync(dbContextMock.Object, "opId", throwOnErrors: true));

            // Assert.
            //the operation closes cancelled without migrating anything
            Assert.Equal(DbMigrationOperation.Status.Cancelled, dbMigrationOp.CurrentStatus);
            Assert.Contains("doesn't own the db context lock", migrationException.Message, StringComparison.Ordinal);
            docMigrationMock.Verify(
                m => m.MigrateAsync(It.IsAny<int>(), It.IsAny<Func<long, Task>?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Never());
            dbContextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
        }

        [Fact]
        public async Task ExecuteMigrationClosesFailedWhenTheLeaseIsLostMidMigration()
        {
            /* A lost lease cancels the migration steps, but the operation state must still
             * save and report the failure: saving it with the lost lease token would make
             * every failure caused by a takeover unreportable. */

            // Setup.
            using var leaseLostTokenSource = new CancellationTokenSource();
            dbContextLockLeaseMock.Setup(l => l.LeaseLostToken).Returns(leaseLostTokenSource.Token);
            dbContextMock.Setup(c => c.SaveChangesAsync(It.Is<CancellationToken>(t => t.IsCancellationRequested)))
                .ThrowsAsync(new OperationCanceledException());

            var repositoryMock = new Mock<IRepository>();
            repositoryMock.Setup(r => r.Name).Returns("fakeModels");
            var docMigrationMock = new Mock<DocumentMigration>();
            docMigrationMock.Setup(m => m.SourceRepository).Returns(repositoryMock.Object);
            docMigrationMock.Setup(m => m.MigrateAsync(It.IsAny<int>(), It.IsAny<Func<long, Task>?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns<int, Func<long, Task>?, bool, bool, int, CancellationToken>((_, _, _, _, _, cancellationToken) =>
                {
                    //the lease is taken over while the documents migrate
                    leaseLostTokenSource.Cancel();
                    cancellationToken.ThrowIfCancellationRequested();
                    return Task.FromResult(MigrationResult.Succeeded(0));
                });
            dbContextMock.Setup(c => c.DocumentMigrationList).Returns([docMigrationMock.Object]);

            // Action.
            await dbMigrationManager.ExecuteDbContextMigrationAsync(dbContextMock.Object, "opId");

            // Assert.
            Assert.Equal(DbMigrationOperation.Status.Failed, dbMigrationOp.CurrentStatus);
            dbContextMock.Verify(
                c => c.SaveChangesAsync(It.Is<CancellationToken>(t => !t.IsCancellationRequested)),
                Times.AtLeastOnce());
        }

        [Fact]
        public async Task ExecuteMigrationCompletesOperation()
        {
            // Action.
            await dbMigrationManager.ExecuteDbContextMigrationAsync(dbContextMock.Object, "opId", "taskId");

            // Assert.
            Assert.Equal(DbMigrationOperation.Status.Completed, dbMigrationOp.CurrentStatus);
            Assert.NotNull(dbMigrationOp.CompletedDateTime);
            Assert.Equal("taskId", dbMigrationOp.TaskId);
            dbContextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce());
        }

        [Fact]
        public async Task ExecuteMigrationFailsOnStepError()
        {
            // Setup.
            var repositoryMock = new Mock<IRepository>();
            repositoryMock.Setup(r => r.Name).Returns("fakeModels");
            repositoryMock.Setup(r => r.DeleteOldIndexesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException());
            repositoryRegistryMock.Setup(r => r.Repositories).Returns([repositoryMock.Object]);

            // Action.
            await dbMigrationManager.ExecuteDbContextMigrationAsync(dbContextMock.Object, "opId");

            // Assert.
            Assert.Equal(DbMigrationOperation.Status.Failed, dbMigrationOp.CurrentStatus);
            Assert.Contains(dbMigrationOp.Logs, log => log is DeleteOldIndexesMigrationLog { State: MigrationLogBase.ExecutionState.Failed });
        }

        [Fact]
        public async Task ExecuteMigrationFailsOnUnhandledException()
        {
            // Setup.
            var docMigrationMock = new Mock<DocumentMigration>();
            docMigrationMock.Setup(m => m.MigrateAsync(It.IsAny<int>(), It.IsAny<Func<long, Task>?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Unhandled migration exception"));
            dbContextMock.Setup(c => c.DocumentMigrationList).Returns([docMigrationMock.Object]);

            // Action.
            await dbMigrationManager.ExecuteDbContextMigrationAsync(dbContextMock.Object, "opId");

            // Assert.
            //the operation doesn't stay on running status, and the failure is persisted
            Assert.Equal(DbMigrationOperation.Status.Failed, dbMigrationOp.CurrentStatus);
            dbContextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce());
        }

        [Fact]
        public async Task ExecuteMigrationForwardsLostLeaseCancellation()
        {
            // Setup.
            using var leaseLostTokenSource = new CancellationTokenSource();
            dbContextLockLeaseMock.Setup(l => l.LeaseLostToken).Returns(leaseLostTokenSource.Token);

            var repositoryMock = new Mock<IRepository>();
            repositoryMock.Setup(r => r.Name).Returns("fakeModels");
            repositoryRegistryMock.Setup(r => r.Repositories).Returns([repositoryMock.Object]);
            var docMigrationMock = new Mock<DocumentMigration>();
            docMigrationMock.Setup(m => m.SourceRepository).Returns(repositoryMock.Object);
            docMigrationMock.Setup(m => m.MigrateAsync(It.IsAny<int>(), It.IsAny<Func<long, Task>?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(MigrationResult.Succeeded(1));
            dbContextMock.Setup(c => c.DocumentMigrationList).Returns([docMigrationMock.Object]);

            // Action.
            await dbMigrationManager.ExecuteDbContextMigrationAsync(dbContextMock.Object, "opId");

            // Assert.
            //every step observes the lease lost token, aborting work run without exclusivity
            docMigrationMock.Verify(
                m => m.MigrateAsync(It.IsAny<int>(), It.IsAny<Func<long, Task>?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<int>(), leaseLostTokenSource.Token),
                Times.Once());
            repositoryMock.Verify(r => r.DeleteOldIndexesAsync(leaseLostTokenSource.Token), Times.Once());
            repositoryMock.Verify(r => r.BuildNewIndexesAsync(leaseLostTokenSource.Token), Times.Once());
        }

        [Fact]
        public async Task ExecuteMigrationPassesStopAtFirstErrorToDocumentMigrations()
        {
            // Setup.
            var stopAtFirstErrorOp = new DbMigrationOperation(engineMock.Object, isStopAtFirstErrorEnabled: true);
            dbOperationsMock.Setup(r => r.FindOneAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(stopAtFirstErrorOp);

            var repositoryMock = new Mock<IRepository>();
            repositoryMock.Setup(r => r.Name).Returns("fakeModels");
            var docMigrationMock = new Mock<DocumentMigration>();
            docMigrationMock.Setup(m => m.SourceRepository).Returns(repositoryMock.Object);
            docMigrationMock.Setup(m => m.MigrateAsync(It.IsAny<int>(), It.IsAny<Func<long, Task>?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(MigrationResult.Succeeded(3));
            dbContextMock.Setup(c => c.DocumentMigrationList).Returns([docMigrationMock.Object]);

            // Action.
            await dbMigrationManager.ExecuteDbContextMigrationAsync(dbContextMock.Object, "opId");

            // Assert.
            docMigrationMock.Verify(m => m.MigrateAsync(It.IsAny<int>(), It.IsAny<Func<long, Task>?>(), false, true, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once());
        }

        [Fact]
        public async Task ExecuteMigrationReleasesLockLease()
        {
            // Action.
            await dbMigrationManager.ExecuteDbContextMigrationAsync(dbContextMock.Object, "opId");

            // Assert.
            //the execution resumes the claim of the operation, and releases it at completion
            dbContextLockMock.Verify(l => l.TryResumeClaimAsync("opId"), Times.Once());
            dbContextLockLeaseMock.Verify(l => l.DisposeAsync(), Times.Once());
        }

        [Fact]
        public async Task ExecuteMigrationReportsDocumentProgressOnRollingLog()
        {
            // Setup.
            var repositoryMock = new Mock<IRepository>();
            repositoryMock.Setup(r => r.Name).Returns("fakeModels");
            var progressSnapshots = new List<(int ExecutingLogs, long TotMigratedDocs)>();
            var docMigrationMock = new Mock<DocumentMigration>();
            docMigrationMock.Setup(m => m.SourceRepository).Returns(repositoryMock.Object);
            docMigrationMock.Setup(m => m.MigrateAsync(It.IsAny<int>(), It.IsAny<Func<long, Task>?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns<int, Func<long, Task>?, bool, bool, int, CancellationToken>(async (_, callbackAsync, _, _, _, _) =>
                {
                    //three periodic progress reports, like a long collection scan raises
                    foreach (var migratedDocs in new[] { 500L, 1000L, 1500L })
                    {
                        await callbackAsync!(migratedDocs);

                        var executingLogs = dbMigrationOp.Logs.OfType<DocumentMigrationLog>()
                            .Where(log => log.State == MigrationLogBase.ExecutionState.Executing)
                            .ToList();
                        progressSnapshots.Add((executingLogs.Count, executingLogs.Last().TotMigratedDocs));
                    }
                    return MigrationResult.Succeeded(1500);
                });
            dbContextMock.Setup(c => c.DocumentMigrationList).Returns([docMigrationMock.Object]);

            // Action.
            await dbMigrationManager.ExecuteDbContextMigrationAsync(dbContextMock.Object, "opId");

            // Assert.
            //each periodic report updates one rolling executing log, with the running counter
            List<(int ExecutingLogs, long TotMigratedDocs)> expectedSnapshots = [(1, 500), (1, 1000), (1, 1500)];
            Assert.Equal(expectedSnapshots, progressSnapshots);

            //the ended log replaces the rolling one: the operation keeps a single documents log
            Assert.Equal(DbMigrationOperation.Status.Completed, dbMigrationOp.CurrentStatus);
            var documentLog = Assert.IsType<DocumentMigrationLog>(Assert.Single(dbMigrationOp.Logs));
            Assert.Equal(MigrationLogBase.ExecutionState.Succeded, documentLog.State);
            Assert.Equal(1500, documentLog.TotMigratedDocs);
        }

        [Fact]
        public async Task ExecuteMigrationSkipsIndexStepsOnReadOnlyRepositories()
        {
            // Setup.
            var readOnlyRepositoryMock = new Mock<IRepository>();
            readOnlyRepositoryMock.Setup(r => r.IsReadOnly).Returns(true);
            readOnlyRepositoryMock.Setup(r => r.Name).Returns("readOnlyModels");
            var writableRepositoryMock = new Mock<IRepository>();
            writableRepositoryMock.Setup(r => r.Name).Returns("writableModels");
            repositoryRegistryMock.Setup(r => r.Repositories)
                .Returns([readOnlyRepositoryMock.Object, writableRepositoryMock.Object]);

            // Action.
            await dbMigrationManager.ExecuteDbContextMigrationAsync(dbContextMock.Object, "opId");

            // Assert.
            Assert.Equal(DbMigrationOperation.Status.Completed, dbMigrationOp.CurrentStatus);
            readOnlyRepositoryMock.Verify(r => r.DeleteOldIndexesAsync(It.IsAny<CancellationToken>()), Times.Never());
            readOnlyRepositoryMock.Verify(r => r.BuildNewIndexesAsync(It.IsAny<CancellationToken>()), Times.Never());
            writableRepositoryMock.Verify(r => r.DeleteOldIndexesAsync(It.IsAny<CancellationToken>()), Times.Once());
            writableRepositoryMock.Verify(r => r.BuildNewIndexesAsync(It.IsAny<CancellationToken>()), Times.Once());
            Assert.DoesNotContain(dbMigrationOp.Logs, log => log is DeleteOldIndexesMigrationLog { Repository: "readOnlyModels" });
            Assert.DoesNotContain(dbMigrationOp.Logs, log => log is BuildNewIndexesMigrationLog { Repository: "readOnlyModels" });
            Assert.Contains(dbMigrationOp.Logs, log => log is DeleteOldIndexesMigrationLog { Repository: "writableModels" });
            Assert.Contains(dbMigrationOp.Logs, log => log is BuildNewIndexesMigrationLog { Repository: "writableModels" });
        }

        [Fact]
        public async Task ExecuteMigrationThrowsOnReadOnlyDbContext()
        {
            // Setup.
            optionsMock.Setup(o => o.IsReadOnly).Returns(true);

            // Action and assert.
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                dbMigrationManager.ExecuteDbContextMigrationAsync(dbContextMock.Object, "opId"));
            dbContextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
        }

        [Fact]
        public async Task ExecuteMigrationThrowsOnUnhandledExceptionWhenRequired()
        {
            // Setup.
            var unhandledException = new InvalidOperationException("Unhandled migration exception");
            var docMigrationMock = new Mock<DocumentMigration>();
            docMigrationMock.Setup(m => m.MigrateAsync(It.IsAny<int>(), It.IsAny<Func<long, Task>?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(unhandledException);
            dbContextMock.Setup(c => c.DocumentMigrationList).Returns([docMigrationMock.Object]);

            // Action.
            var migrationException = await Assert.ThrowsAsync<ScriniumDbMigrationException>(() =>
                dbMigrationManager.ExecuteDbContextMigrationAsync(dbContextMock.Object, "opId", throwOnErrors: true));

            // Assert.
            Assert.Equal(DbMigrationOperation.Status.Failed, dbMigrationOp.CurrentStatus);
            Assert.Contains("FakeDbContext", migrationException.Message, StringComparison.Ordinal);
            var aggregateException = Assert.IsType<AggregateException>(migrationException.InnerException);
            Assert.Contains(unhandledException, aggregateException.InnerExceptions);
        }

        [Fact]
        public async Task ExecuteMigrationUsesAmbientLockLease()
        {
            // Setup.
            //an outer flow (e.g. seeding) already holds a lease on the db context lock
            var ambientLeaseMock = new Mock<IResourceLockLease>();
            dbContextLockMock.Setup(l => l.TryGetAmbientLease())
                .Returns(ambientLeaseMock.Object);

            // Action.
            await dbMigrationManager.ExecuteDbContextMigrationAsync(dbContextMock.Object, "opId");

            // Assert.
            //the ambient lease belongs to its outer flow: it is neither resumed nor released here
            Assert.Equal(DbMigrationOperation.Status.Completed, dbMigrationOp.CurrentStatus);
            dbContextLockMock.Verify(l => l.TryResumeClaimAsync(It.IsAny<string>()), Times.Never());
            ambientLeaseMock.Verify(l => l.DisposeAsync(), Times.Never());
        }

        [Fact]
        public async Task ExecuteMigrationWithoutLockClaimLeavesAClosedOperationClosed()
        {
            /* A closed operation reopened by a late execution (a task runner delivering it
             * twice) would report a state it never returns to. */

            // Setup.
            var completedOp = new DbMigrationOperation(engineMock.Object);
            completedOp.TaskStarted();
            completedOp.TaskCompleted();
            dbOperationsMock.Setup(r => r.FindOneAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(completedOp);
            dbContextLockMock.Setup(l => l.TryResumeClaimAsync(It.IsAny<string>()))
                .ReturnsAsync((IResourceLockLease?)null);

            // Action.
            await dbMigrationManager.ExecuteDbContextMigrationAsync(dbContextMock.Object, "opId");

            // Assert.
            Assert.Equal(DbMigrationOperation.Status.Completed, completedOp.CurrentStatus);
            dbContextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
        }

        [Fact]
        public async Task TryStartMigrationClosesOrphanedOperations()
        {
            // Action.
            var migrationOp = await dbMigrationManager.TryStartDbContextMigrationAsync(dbContextMock.Object);

            // Assert.
            //after the claim, the operations orphaned by dead owners close directly on the server
            Assert.NotNull(migrationOp);
            dbOperationsMock.Verify(
                r => r.UpdateManyAsync(
                    It.IsAny<FilterDefinition<OperationBase>>(),
                    It.IsAny<UpdateDefinition<OperationBase>>(),
                    It.IsAny<UpdateOptions?>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }

        [Fact]
        public async Task TryStartMigrationCreatesDryRunOperation()
        {
            // Action.
            var migrationOp = await dbMigrationManager.TryStartDbContextMigrationAsync(dbContextMock.Object, dryRun: true);

            // Assert.
            Assert.NotNull(migrationOp);
            Assert.True(migrationOp.IsDryRun);
            Assert.False(migrationOp.IsStopAtFirstErrorEnabled);
            dbOperationsMock.Verify(
                r => r.CreateAsync(migrationOp, It.IsAny<CancellationToken>()),
                Times.Once());
            dbContextLockMock.Verify(
                l => l.TryClaimAsync(migrationOp.Id, It.IsAny<TimeSpan?>()),
                Times.Once());
            taskRunnerMock.Verify(
                t => t.RunMigrateDbTask(It.IsAny<Type>(), migrationOp.Id),
                Times.Once());
        }

        [Fact]
        public async Task TryStartMigrationCreatesStopAtFirstErrorOperation()
        {
            // Action.
            var migrationOp = await dbMigrationManager.TryStartDbContextMigrationAsync(
                dbContextMock.Object, stopAtFirstError: true);

            // Assert.
            Assert.NotNull(migrationOp);
            Assert.False(migrationOp.IsDryRun);
            Assert.True(migrationOp.IsStopAtFirstErrorEnabled);
        }

        [Fact]
        public async Task TryStartMigrationClaimsTheLockWithItsLeaseDuration()
        {
            /* The lease of the start covers the window before the background task runner picks
             * the operation up, the only one nothing renews it. */

            // Setup.
            var chosenLeaseDuration = TimeSpan.FromMinutes(30);

            // Action.
            var migrationOp = await dbMigrationManager.TryStartDbContextMigrationAsync(
                dbContextMock.Object, lockLeaseDuration: chosenLeaseDuration);

            // Assert.
            Assert.NotNull(migrationOp);
            dbContextLockMock.Verify(
                l => l.TryClaimAsync(migrationOp.Id, chosenLeaseDuration),
                Times.Once());
        }

        [Fact]
        public async Task TryStartMigrationClaimsWithoutALeaseDurationByDefault()
        {
            // Action.
            var migrationOp = await dbMigrationManager.TryStartDbContextMigrationAsync(dbContextMock.Object);

            // Assert.
            //no duration to the claim: the lock applies its own default
            Assert.NotNull(migrationOp);
            dbContextLockMock.Verify(
                l => l.TryClaimAsync(migrationOp.Id, null),
                Times.Once());
        }

        [Fact]
        public async Task TryStartMigrationDeniesOnReadOnlyDbContext()
        {
            // Setup.
            optionsMock.Setup(o => o.IsReadOnly).Returns(true);

            // Action.
            var migrationOp = await dbMigrationManager.TryStartDbContextMigrationAsync(dbContextMock.Object);

            // Assert.
            Assert.Null(migrationOp);
            dbContextLockMock.Verify(l => l.TryClaimAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()), Times.Never());
            dbOperationsMock.Verify(
                r => r.CreateAsync(It.IsAny<OperationBase>(), It.IsAny<CancellationToken>()),
                Times.Never());
        }

        [Fact]
        public async Task TryStartMigrationDeniesWhenLockIsHeld()
        {
            // Setup.
            //another owner (a queued or running migration, or a seeding) holds the db context lock
            dbContextLockMock.Setup(l => l.TryClaimAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
                .ReturnsAsync(false);

            // Action.
            var migrationOp = await dbMigrationManager.TryStartDbContextMigrationAsync(dbContextMock.Object);

            // Assert.
            //the losing operation deletes itself, and no migration task is scheduled
            Assert.Null(migrationOp);
            dbOperationsMock.Verify(
                r => r.CreateAsync(It.IsAny<OperationBase>(), It.IsAny<CancellationToken>()),
                Times.Once());
            dbOperationsMock.Verify(
                r => r.DeleteAsync(
                    It.IsAny<OperationBase>(),
                    It.IsAny<FilterDefinition<OperationBase>[]?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once());
            taskRunnerMock.Verify(
                t => t.RunMigrateDbTask(It.IsAny<Type>(), It.IsAny<string>()),
                Times.Never());
        }

        [Fact]
        public async Task TryStartMigrationDeniesWhenTheDeniedOperationCantBeDeleted()
        {
            // Setup.
            //another owner holds the db context lock, and the losing operation can't be deleted
            dbContextLockMock.Setup(l => l.TryClaimAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
                .ReturnsAsync(false);
            dbOperationsMock.Setup(r => r.DeleteAsync(
                    It.IsAny<OperationBase>(),
                    It.IsAny<FilterDefinition<OperationBase>[]?>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException());

            // Action.
            var migrationOp = await dbMigrationManager.TryStartDbContextMigrationAsync(dbContextMock.Object);

            // Assert.
            //the start reports the denial, instead of the failure of its own cleanup
            Assert.Null(migrationOp);
            taskRunnerMock.Verify(
                t => t.RunMigrateDbTask(It.IsAny<Type>(), It.IsAny<string>()),
                Times.Never());
        }

        [Fact]
        public async Task TryStartMigrationDeniesWithInProcessExclusiveAccess()
        {
            // Setup.
            //another flow of this process already holds the exclusive access on the db context
            engineMock.Setup(e => e.IsExclusiveWriteEnabled).Returns(true);

            // Action.
            var migrationOp = await dbMigrationManager.TryStartDbContextMigrationAsync(dbContextMock.Object);

            // Assert.
            //the denial comes before any write: no operation created, no claim attempted
            Assert.Null(migrationOp);
            dbContextLockMock.Verify(l => l.TryClaimAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()), Times.Never());
            dbOperationsMock.Verify(
                r => r.CreateAsync(It.IsAny<OperationBase>(), It.IsAny<CancellationToken>()),
                Times.Never());
            taskRunnerMock.Verify(
                t => t.RunMigrateDbTask(It.IsAny<Type>(), It.IsAny<string>()),
                Times.Never());
        }

        [Fact]
        public async Task TryStartMigrationReleasesTheClaimWhenClosingOrphanedOperationsFails()
        {
            /* A claim held by an operation whose task never runs would deny every migration
             * and seeding of the db context until its lease expiration. */

            // Setup.
            //another flow entered the exclusive access between the read and the orphans sweep
            dbOperationsMock.Setup(r => r.UpdateManyAsync(
                    It.IsAny<FilterDefinition<OperationBase>>(),
                    It.IsAny<UpdateDefinition<OperationBase>>(),
                    It.IsAny<UpdateOptions?>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new UnauthorizedAccessException());

            // Action and assert.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                dbMigrationManager.TryStartDbContextMigrationAsync(dbContextMock.Object));
            dbContextLockMock.Verify(l => l.TryReleaseAsync(It.IsAny<string>()), Times.Once());
            dbOperationsMock.Verify(
                r => r.DeleteAsync(
                    It.IsAny<OperationBase>(),
                    It.IsAny<FilterDefinition<OperationBase>[]?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once());
        }

        [Fact]
        public async Task TryStartMigrationReleasesTheClaimWhenTheTaskEnqueueFails()
        {
            // Setup.
            //the task storage is unreachable: the migration task will never run
            taskRunnerMock.Setup(t => t.RunMigrateDbTask(It.IsAny<Type>(), It.IsAny<string>()))
                .Throws(new InvalidOperationException("Task storage unreachable"));

            // Action and assert.
            var startException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                dbMigrationManager.TryStartDbContextMigrationAsync(dbContextMock.Object));

            //the failure reaches the caller, with the lock released and the operation dropped
            Assert.Equal("Task storage unreachable", startException.Message);
            dbContextLockMock.Verify(l => l.TryReleaseAsync(It.IsAny<string>()), Times.Once());
            dbOperationsMock.Verify(
                r => r.DeleteAsync(
                    It.IsAny<OperationBase>(),
                    It.IsAny<FilterDefinition<OperationBase>[]?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once());
        }
    }
}
