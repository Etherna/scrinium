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

using Etherna.Scrinium.Core.Domain.Models.DbMigrationOpAgg;
using Xunit;

namespace Etherna.Scrinium.Core.Migration
{
    public class MigrationResultTest
    {
        // Tests.
        [Fact]
        public void ProcessedDocumentsCountsMigratedAndFailingDocuments()
        {
            // Action.
            var result = MigrationResult.Failed(
                10,
                documentErrors: [new DocumentMigrationError("doc1", "FormatException: bad value")],
                totDocumentErrors: 2);

            // Assert.
            Assert.Equal(12, result.ProcessedDocuments);
        }

        [Fact]
        public void ProcessedDocumentsEqualsMigratedDocumentsWithoutErrors()
        {
            // Action.
            var result = MigrationResult.Succeeded(10);

            // Assert.
            Assert.Equal(10, result.ProcessedDocuments);
        }
    }
}
