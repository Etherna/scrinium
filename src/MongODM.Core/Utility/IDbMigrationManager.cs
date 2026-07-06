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
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core.Utility
{
    public interface IDbMigrationManager : IDbContextInitializable
    {
        /// <summary>
        /// Execute a db context migration process: delete old indexes, migrate documents, and build new indexes.
        /// The caller must already hold an exclusive access on the db context.
        /// </summary>
        /// <param name="dbMigrationOpId">Id of the migration operation to execute</param>
        /// <param name="taskId">Optional id of the background task running the migration</param>
        /// <param name="throwOnErrors">If true, throw an exception when the migration completes with errors</param>
        Task ExecuteDbContextMigrationAsync(string dbMigrationOpId, string? taskId = null, bool throwOnErrors = false);

        Task<DbMigrationOperation?> IsMigrationRunningAsync();

        Task<List<DbMigrationOperation>> GetLastMigrationsAsync(int page, int take);

        Task<DbMigrationOperation> GetMigrationAsync(string migrateOperationId);

        /// <summary>
        /// Try to start a db context migration process, if no other migration is queued or running.
        /// </summary>
        /// <returns>The new migration operation, or null if another one is already in progress</returns>
        Task<DbMigrationOperation?> TryStartDbContextMigrationAsync();
    }
}