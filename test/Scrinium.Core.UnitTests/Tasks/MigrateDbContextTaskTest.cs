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

using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Repositories;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.Core.Tasks
{
    public class MigrateDbContextTaskTest
    {
        // Fields.
        private readonly MigrateDbContextTask task;

        private readonly Mock<IDbContext> dbContextMock = new();
        private readonly Mock<IRepository<OperationBase, string>> dbOperationsMock = new();
        private readonly Mock<IDbContextEngine> engineMock = new();

        // Constructor.
        public MigrateDbContextTaskTest()
        {
            engineMock.Setup(e => e.Identifier).Returns("FakeDbContext");
            engineMock.Setup(e => e.RunWithExclusiveAccessAsync(It.IsAny<Func<Task>>(), It.IsAny<bool>()))
                .Returns<Func<Task>, bool>((action, _) => action());

            dbContextMock.Setup(c => c.DbOperations).Returns(dbOperationsMock.Object);
            dbContextMock.Setup(c => c.Engine).Returns(engineMock.Object);

            var serviceProviderMock = new Mock<IServiceProvider>();
            serviceProviderMock.Setup(p => p.GetService(typeof(IDbContext))).Returns(dbContextMock.Object);

            task = new MigrateDbContextTask(serviceProviderMock.Object);
        }

        // Tests.
        [Fact]
        public async Task RunDryRunOperationWithoutExclusiveAccess()
        {
            // Setup.
            var dryRunOp = new DbMigrationOperation(engineMock.Object, isDryRun: true);
            dbOperationsMock.Setup(r => r.FindOneAsync("opId", It.IsAny<CancellationToken>()))
                .ReturnsAsync(dryRunOp);

            // Action.
            await task.RunAsync<IDbContext>("opId", "taskId");

            // Assert.
            engineMock.Verify(
                e => e.RunWithExclusiveAccessAsync(It.IsAny<Func<Task>>(), It.IsAny<bool>()),
                Times.Never());
            dbContextMock.Verify(c => c.ExecuteMigrationAsync("opId", "taskId", false), Times.Once());
        }

        [Fact]
        public async Task RunMigrationOperationWithExclusiveAccess()
        {
            // Setup.
            var migrationOp = new DbMigrationOperation(engineMock.Object);
            dbOperationsMock.Setup(r => r.FindOneAsync("opId", It.IsAny<CancellationToken>()))
                .ReturnsAsync(migrationOp);

            // Action.
            await task.RunAsync<IDbContext>("opId", "taskId");

            // Assert.
            engineMock.Verify(
                e => e.RunWithExclusiveAccessAsync(It.IsAny<Func<Task>>(), It.IsAny<bool>()),
                Times.Once());
            dbContextMock.Verify(c => c.ExecuteMigrationAsync("opId", "taskId", false), Times.Once());
        }
    }
}
