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

using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.Domain.Models.DbMigrationOpAgg;
using Etherna.MongODM.Core.Exceptions;
using Etherna.MongODM.Core.Migration;
using Etherna.MongODM.Core.Options;
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.Core.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using System;
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
        private readonly Mock<IDbContextOptions> optionsMock = new();
        private readonly Mock<IRepositoryRegistry> repositoryRegistryMock = new();

        // Constructor.
        public DbMigrationManagerTest()
        {
            optionsMock.Setup(o => o.DbName).Returns("test-db");
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

            dbMigrationManager = new DbMigrationManager(new Mock<ITaskRunner>().Object);
            dbMigrationManager.Initialize(engineMock.Object, new Mock<ILogger>().Object);
        }

        // Tests.
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
            docMigrationMock.Setup(m => m.MigrateAsync(It.IsAny<int>(), It.IsAny<Func<long, Task>?>(), It.IsAny<CancellationToken>()))
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
            docMigrationMock.Setup(m => m.MigrateAsync(It.IsAny<int>(), It.IsAny<Func<long, Task>?>(), It.IsAny<CancellationToken>()))
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
