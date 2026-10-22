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

using System;
using Xunit;

namespace Etherna.Scrinium.Core.Domain.Models.DbMigrationOpAgg
{
    public class MigrationLogBaseTest
    {
        [Fact]
        public void InterruptClosesAnExecutingLogKeepingWhatItReported()
        {
            // Setup.
            var log = new DocumentMigrationLog(
                "models",
                MigrationLogBase.ExecutionState.Executing,
                500,
                [new DocumentMigrationError("doc1", "FormatException: bad value")],
                1);
            var creationDateTime = log.CreationDateTime;

            // Action.
            log.Interrupt();

            // Assert.
            //the same entry closes: its instant, collection and counters are the ones it reported
            Assert.Equal(MigrationLogBase.ExecutionState.Interrupted, log.State);
            Assert.Equal(creationDateTime, log.CreationDateTime);
            Assert.Equal("models", log.CollectionName);
            Assert.Equal(500, log.TotMigratedDocs);
            Assert.Equal(1, log.TotErrorDocs);
            Assert.Single(log.Errors);
        }

        [Fact]
        public void InterruptDeniesAnEndedLogNamingItsState()
        {
            // Setup.
            var log = new BuildNewIndexesMigrationLog("models", MigrationLogBase.ExecutionState.Succeeded);

            // Action.
            var exception = Assert.Throws<InvalidOperationException>(log.Interrupt);

            // Assert.
            Assert.Contains("Succeeded", exception.Message, StringComparison.Ordinal);
            Assert.Equal(MigrationLogBase.ExecutionState.Succeeded, log.State);
        }
    }
}
