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

using Etherna.MongoDB.Driver;
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Domain.Models.ReferencesRepairOpAgg;
using Etherna.Scrinium.Core.ExecContext;
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
    public class ReferencesRepairManagerTest
    {
        // Fields.
        private readonly ReferencesRepairManager referencesRepairManager;
        private readonly ReferencesRepairOperation repairOp;

        private readonly Mock<IResourceLockLease> dbContextLockLeaseMock = new();
        private readonly Mock<IResourceLock> dbContextLockMock = new();
        private readonly Mock<IDbContext> dbContextMock = new();
        private readonly Mock<IRepository<OperationBase, string>> dbOperationsMock = new();
        private readonly Mock<IDbContextEngine> engineMock = new();
        private readonly Mock<IExecutionContext> executionContextMock = new();
        private readonly Mock<IDbContextOptions> optionsMock = new();
        private readonly Mock<IRepository> repositoryMock = new();
        private readonly Mock<IRepositoryRegistry> repositoryRegistryMock = new();
        private readonly Mock<ITaskRunner> taskRunnerMock = new();

        // Constructor.
        public ReferencesRepairManagerTest()
        {
            executionContextMock.Setup(c => c.Items).Returns(new Dictionary<object, object?>());
            optionsMock.Setup(o => o.DbName).Returns("test-db");
            dbContextLockMock.Setup(l => l.TryResumeClaimAsync(It.IsAny<string>()))
                .ReturnsAsync(dbContextLockLeaseMock.Object);
            engineMock.Setup(e => e.DbContextLock).Returns(dbContextLockMock.Object);
            engineMock.Setup(e => e.ExecutionContext).Returns(executionContextMock.Object);
            engineMock.Setup(e => e.Identifier).Returns("FakeDbContext");
            engineMock.Setup(e => e.Options).Returns(optionsMock.Object);

            repairOp = new ReferencesRepairOperation(
                engineMock.Object,
                "notes",
                new Dictionary<string, OriginDeleteMode>
                {
                    ["Author"] = OriginDeleteMode.RemoveReference,
                    ["Editor"] = OriginDeleteMode.RemoveReference
                });
            dbOperationsMock.Setup(r => r.FindOneAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(repairOp);
            dbOperationsMock.Setup(r => r.UpdateManyAsync(
                    It.IsAny<FilterDefinition<OperationBase>>(),
                    It.IsAny<UpdateDefinition<OperationBase>>(),
                    It.IsAny<UpdateOptions?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UpdateResult.Acknowledged(0, 0, null));

            repositoryMock.Setup(r => r.Name).Returns("notes");
            repositoryRegistryMock.Setup(r => r.Repositories).Returns([repositoryMock.Object]);

            dbContextMock.Setup(c => c.DbOperations).Returns(dbOperationsMock.Object);
            dbContextMock.Setup(c => c.Engine).Returns(engineMock.Object);
            dbContextMock.Setup(c => c.RepositoryRegistry).Returns(repositoryRegistryMock.Object);

            referencesRepairManager = new ReferencesRepairManager(taskRunnerMock.Object);
            referencesRepairManager.Initialize(engineMock.Object, new Mock<ILogger>().Object);
        }

        // Tests.
        [Fact]
        public async Task ExecuteReferencesRepairFailsTheOpenPathsWithTheErrorStoppingTheRepair()
        {
            // Setup.
            repositoryMock.Setup(r => r.RepairMissingOriginReferencesAsync(
                    It.IsAny<IReadOnlyDictionary<string, OriginDeleteMode>?>(), It.IsAny<bool>(),
                    It.IsAny<Func<MissingOriginReferencesPathRepair, Task>?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("The cursor timed out"));

            // Action.
            await referencesRepairManager.ExecuteReferencesRepairAsync(dbContextMock.Object, "opId");

            // Assert.
            //the repair fails whole: every path still open records the error, the operation its reason
            Assert.Equal(ReferencesRepairOperation.Status.Failed, repairOp.CurrentStatus);
            Assert.Equal("TimeoutException: The cursor timed out", repairOp.FailureReason);
            Assert.All(repairOp.PathStates, pathState =>
            {
                Assert.Equal(ReferencesRepairPathState.ExecutionState.Failed, pathState.State);
                Assert.Equal("TimeoutException: The cursor timed out", pathState.ErrorMessage);
            });
        }

        [Fact]
        public async Task ExecuteReferencesRepairLeavesThePathsToTheInterruptionWhenTheTokenCancelsTheRepair()
        {
            /* A repair cancelled through its token (the lost lease, or an abort) is interrupted,
             * not failed by its paths: they stay as they are, and the transition closing the
             * operation interrupts the executing ones, with no error message of their own. */

            // Setup.
            using var leaseLostTokenSource = new CancellationTokenSource();
            dbContextLockLeaseMock.Setup(l => l.LeaseLostToken).Returns(leaseLostTokenSource.Token);
            repositoryMock.Setup(r => r.RepairMissingOriginReferencesAsync(
                    It.IsAny<IReadOnlyDictionary<string, OriginDeleteMode>?>(), It.IsAny<bool>(),
                    It.IsAny<Func<MissingOriginReferencesPathRepair, Task>?>(), It.IsAny<CancellationToken>()))
                .Returns<IReadOnlyDictionary<string, OriginDeleteMode>?, bool, Func<MissingOriginReferencesPathRepair, Task>?, CancellationToken>(
                    async (_, _, progressAsync, cancellationToken) =>
                    {
                        //the first path reported progress when the lease is taken over
                        await progressAsync!(new MissingOriginReferencesPathRepair("Author", OriginDeleteMode.RemoveReference, 3, 2, 0));
                        leaseLostTokenSource.Cancel();
                        cancellationToken.ThrowIfCancellationRequested();
                        return new MissingOriginReferencesRepairReport([], []);
                    });

            // Action.
            await referencesRepairManager.ExecuteReferencesRepairAsync(dbContextMock.Object, "opId");

            // Assert.
            Assert.Equal(ReferencesRepairOperation.Status.Failed, repairOp.CurrentStatus);
            Assert.StartsWith(DbOperationLifecycle.LostLeaseFailureReason, repairOp.FailureReason, StringComparison.Ordinal);
            var pathStates = repairOp.PathStates.ToArray();
            Assert.All(pathStates, pathState =>
            {
                Assert.Equal(ReferencesRepairPathState.ExecutionState.Interrupted, pathState.State);
                Assert.Null(pathState.ErrorMessage);
            });
            //the interrupted path keeps what it reported until the stop
            Assert.Equal(3, pathStates[0].MissingOriginIdsCount);
            Assert.Equal(2, pathStates[0].UpdatedDocumentsCount);
        }
    }
}
