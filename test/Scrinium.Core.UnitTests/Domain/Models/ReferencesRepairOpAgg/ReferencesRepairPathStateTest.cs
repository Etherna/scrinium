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
using System;
using Xunit;

namespace Etherna.Scrinium.Core.Domain.Models.ReferencesRepairOpAgg
{
    public class ReferencesRepairPathStateTest
    {
        [Fact]
        public void InterruptClosesAnExecutingPathKeepingWhatItReported()
        {
            // Setup.
            var pathState = new ReferencesRepairPathState(
                "Author",
                OriginDeleteMode.RemoveReference,
                ReferencesRepairPathState.ExecutionState.Executing,
                3, 2, 0, null);

            // Action.
            pathState.Interrupt();

            // Assert.
            Assert.Equal(ReferencesRepairPathState.ExecutionState.Interrupted, pathState.State);
            Assert.Equal("Author", pathState.ElementPath);
            Assert.Equal(OriginDeleteMode.RemoveReference, pathState.RepairMode);
            Assert.Equal(3, pathState.MissingOriginIdsCount);
            Assert.Equal(2, pathState.UpdatedDocumentsCount);
        }

        [Fact]
        public void InterruptDeniesAnEndedPathNamingItsState()
        {
            // Setup.
            var pathState = new ReferencesRepairPathState(
                "Author",
                OriginDeleteMode.RemoveReference,
                ReferencesRepairPathState.ExecutionState.Succeeded,
                1, 1, 0, null);

            // Action.
            var exception = Assert.Throws<InvalidOperationException>(pathState.Interrupt);

            // Assert.
            Assert.Contains("Succeeded", exception.Message, StringComparison.Ordinal);
            Assert.Equal(ReferencesRepairPathState.ExecutionState.Succeeded, pathState.State);
        }
    }
}
