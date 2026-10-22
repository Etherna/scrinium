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

using Etherna.Scrinium.Core.Options;
using Moq;
using System;
using System.Collections.Generic;
using Xunit;

namespace Etherna.Scrinium.Core.Domain.Models
{
    public class ReferencesRepairOperationTest
    {
        // Fields.
        private readonly ReferencesRepairOperation repairOp;

        // Constructor.
        public ReferencesRepairOperationTest()
        {
            var engineMock = new Mock<IDbContextEngine>();
            engineMock.Setup(e => e.Identifier).Returns("FakeDbContext");

            repairOp = new ReferencesRepairOperation(
                engineMock.Object,
                "posts",
                new Dictionary<string, OriginDeleteMode> { ["Author"] = OriginDeleteMode.RemoveReference });
        }

        // Tests.
        [Fact]
        public void TaskFailedRecordsTheReasonAndTheClosingInstant()
        {
            // Setup.
            repairOp.TaskStarted("task");

            // Action.
            repairOp.TaskFailed("TimeoutException: the cursor timed out");

            // Assert.
            Assert.Equal(ReferencesRepairOperation.Status.Failed, repairOp.CurrentStatus);
            Assert.Equal("TimeoutException: the cursor timed out", repairOp.FailureReason);
            Assert.NotNull(repairOp.CompletedDateTime);
        }

        [Fact]
        public void TaskStartedDeniesARunningOperationNamingItsStatus()
        {
            // Setup.
            repairOp.TaskStarted("task");

            // Action.
            var exception = Assert.Throws<InvalidOperationException>(() => repairOp.TaskStarted("retry"));

            // Assert.
            Assert.Contains("start the references repair operation", exception.Message, StringComparison.Ordinal);
            Assert.Contains("status is Running", exception.Message, StringComparison.Ordinal);
        }
    }
}
