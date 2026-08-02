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
using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.Domain.Models.DbMigrationOpAgg;
using Etherna.MongODM.Core.Exceptions;
using Etherna.MongODM.Core.ExecContext;
using Etherna.MongODM.Core.Migration;
using Etherna.MongODM.Core.Options;
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.Core.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.MongODM.Core.Utility
{
    public class DbMigrationManagerTest
    {
        // Fields.
        private readonly DbMigrationManager dbMigrationManager;
        private readonly DbMigrationOperation dbMigrationOp;

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
            engineMock.Setup(e => e.ExecutionContext).Returns(executionContextMock.Object);
            engineMock.Setup(e => e.Identifier).Returns("FakeDbContext");
            engineMock.Setup(e => e.Options).Returns(optionsMock.Object);

            dbMigrationOp = new DbMigrationOperation(engineMock.Object);
            dbOperationsMock.Setup(r => r.FindOneAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(dbMigrationOp);
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
            docMigrationMock.Setup(m => m.MigrateAsync(It.IsAny<int>(), It.IsAny<Func<long, Task>?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(MigrationResult.Failed(
                    10,
                    documentErrors: [new DocumentMigrationError("doc1", "FormatException: bad value")],
                    totDocumentErrors: 2));
            dbContextMock.Setup(c => c.DocumentMigrationList).Returns([docMigrationMock.Object]);

            // Action.
            var migrationException = await Assert.ThrowsAsync<MongodmDbMigrationException>(() =>
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
            docMigrationMock.Setup(m => m.MigrateAsync(It.IsAny<int>(), It.IsAny<Func<long, Task>?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(MigrationResult.Succeeded(3));
            dbContextMock.Setup(c => c.DocumentMigrationList).Returns([docMigrationMock.Object]);

            // Action.
            await dbMigrationManager.ExecuteDbContextMigrationAsync(dbContextMock.Object, "opId");

            // Assert.
            Assert.Equal(DbMigrationOperation.Status.Completed, dryRunOp.CurrentStatus);
            repositoryMock.Verify(r => r.DeleteOldIndexesAsync(It.IsAny<CancellationToken>()), Times.Never());
            repositoryMock.Verify(r => r.BuildNewIndexesAsync(It.IsAny<CancellationToken>()), Times.Never());
            docMigrationMock.Verify(m => m.MigrateAsync(It.IsAny<int>(), It.IsAny<Func<long, Task>?>(), true, false, It.IsAny<CancellationToken>()), Times.Once());
            Assert.DoesNotContain(dryRunOp.Logs, log => log is DeleteOldIndexesMigrationLog or BuildNewIndexesMigrationLog);
            Assert.Contains(dryRunOp.Logs, log => log is DocumentMigrationLog { State: MigrationLogBase.ExecutionState.Succeded, TotMigratedDocs: 3 });
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
            docMigrationMock.Setup(m => m.MigrateAsync(It.IsAny<int>(), It.IsAny<Func<long, Task>?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
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
            docMigrationMock.Setup(m => m.MigrateAsync(It.IsAny<int>(), It.IsAny<Func<long, Task>?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(MigrationResult.Succeeded(3));
            dbContextMock.Setup(c => c.DocumentMigrationList).Returns([docMigrationMock.Object]);

            // Action.
            await dbMigrationManager.ExecuteDbContextMigrationAsync(dbContextMock.Object, "opId");

            // Assert.
            docMigrationMock.Verify(m => m.MigrateAsync(It.IsAny<int>(), It.IsAny<Func<long, Task>?>(), false, true, It.IsAny<CancellationToken>()), Times.Once());
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
            docMigrationMock.Setup(m => m.MigrateAsync(It.IsAny<int>(), It.IsAny<Func<long, Task>?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(unhandledException);
            dbContextMock.Setup(c => c.DocumentMigrationList).Returns([docMigrationMock.Object]);

            // Action.
            var migrationException = await Assert.ThrowsAsync<MongodmDbMigrationException>(() =>
                dbMigrationManager.ExecuteDbContextMigrationAsync(dbContextMock.Object, "opId", throwOnErrors: true));

            // Assert.
            Assert.Equal(DbMigrationOperation.Status.Failed, dbMigrationOp.CurrentStatus);
            Assert.Contains("FakeDbContext", migrationException.Message, StringComparison.Ordinal);
            var aggregateException = Assert.IsType<AggregateException>(migrationException.InnerException);
            Assert.Contains(unhandledException, aggregateException.InnerExceptions);
        }

        [Fact]
        public async Task TryStartMigrationCreatesDryRunOperation()
        {
            // Setup.
            //no migration is queued or running
            dbOperationsMock.Setup(r => r.QueryElementsAsync(
                    It.IsAny<Func<IQueryable<OperationBase>, Task<DbMigrationOperation?>>>(),
                    It.IsAny<AggregateOptions?>()))
                .ReturnsAsync((DbMigrationOperation?)null);

            // Action.
            var migrationOp = await dbMigrationManager.TryStartDbContextMigrationAsync(dbContextMock.Object, dryRun: true);

            // Assert.
            Assert.NotNull(migrationOp);
            Assert.True(migrationOp.IsDryRun);
            Assert.False(migrationOp.IsStopAtFirstErrorEnabled);
            dbOperationsMock.Verify(
                r => r.CreateAsync(migrationOp, It.IsAny<CancellationToken>()),
                Times.Once());
            taskRunnerMock.Verify(
                t => t.RunMigrateDbTask(It.IsAny<Type>(), migrationOp.Id),
                Times.Once());
        }

        [Fact]
        public async Task TryStartMigrationCreatesStopAtFirstErrorOperation()
        {
            // Setup.
            //no migration is queued or running
            dbOperationsMock.Setup(r => r.QueryElementsAsync(
                    It.IsAny<Func<IQueryable<OperationBase>, Task<DbMigrationOperation?>>>(),
                    It.IsAny<AggregateOptions?>()))
                .ReturnsAsync((DbMigrationOperation?)null);

            // Action.
            var migrationOp = await dbMigrationManager.TryStartDbContextMigrationAsync(
                dbContextMock.Object, stopAtFirstError: true);

            // Assert.
            Assert.NotNull(migrationOp);
            Assert.False(migrationOp.IsDryRun);
            Assert.True(migrationOp.IsStopAtFirstErrorEnabled);
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
            dbOperationsMock.Verify(
                r => r.CreateAsync(It.IsAny<OperationBase>(), It.IsAny<CancellationToken>()),
                Times.Never());
        }
    }
}
