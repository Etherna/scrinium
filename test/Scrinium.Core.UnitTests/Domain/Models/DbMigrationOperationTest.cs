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

using Etherna.MongODM.Core.Domain.Models.DbMigrationOpAgg;
using Moq;
using System.Linq;
using Xunit;

namespace Etherna.MongODM.Core.Domain.Models
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
        public void AddDocumentMigrationLogKeepsLogsOfOtherCollectionsAndTypes()
        {
            // Setup.
            dbMigrationOp.AddLog(new DeleteOldIndexesMigrationLog(
                "modelsA", MigrationLogBase.ExecutionState.Succeded));
            dbMigrationOp.AddDocumentMigrationLog(new DocumentMigrationLog(
                "modelsA", MigrationLogBase.ExecutionState.Executing, 500));
            dbMigrationOp.AddDocumentMigrationLog(new DocumentMigrationLog(
                "modelsB", MigrationLogBase.ExecutionState.Executing, 100));

            // Action.
            dbMigrationOp.AddDocumentMigrationLog(new DocumentMigrationLog(
                "modelsA", MigrationLogBase.ExecutionState.Succeded, 750));

            // Assert.
            //only the executing progress log of the same collection is replaced
            Assert.Contains(dbMigrationOp.Logs, log => log is DeleteOldIndexesMigrationLog);
            Assert.Contains(dbMigrationOp.Logs, log => log is DocumentMigrationLog
            {
                CollectionName: "modelsA",
                State: MigrationLogBase.ExecutionState.Succeded,
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
    }
}
