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
using Etherna.MongODM.Core.Migration;
using Etherna.MongODM.Core.Repositories;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core
{
    /// <summary>
    /// Interface of <see cref="DbContext"/> implementation. The unit of work over
    /// a scope independent <see cref="IDbContextEngine"/>.
    /// </summary>
    public interface IDbContext
    {
        // Properties.
        /// <summary>
        /// List of models loaded in the current execution scope with pending changes to save.
        /// </summary>
        IReadOnlyCollection<IEntityModel> ChangedModelsList { get; }

        /// <summary>
        /// Internal collection for keep db operations execution log
        /// </summary>
        IRepository<OperationBase, string> DbOperations { get; }

        /// <summary>
        /// List of registered migration tasks
        /// </summary>
        IEnumerable<DocumentMigration> DocumentMigrationList { get; }

        /// <summary>
        /// The scope independent engine serving this db context instance.
        /// </summary>
        IDbContextEngine Engine { get; }

        /// <summary>
        /// True if it has been seeded.
        /// </summary>
        bool IsSeeded { get; }

        /// <summary>
        /// Registry of the repositories of this db context instance.
        /// </summary>
        IRepositoryRegistry RepositoryRegistry { get; }

        // Methods.
        /// <summary>
        /// Execute a db context migration process: delete old indexes, migrate documents, and build new indexes.
        /// The caller must already hold an exclusive access on the db context.
        /// </summary>
        /// <param name="dbMigrationOpId">Id of the migration operation to execute</param>
        /// <param name="taskId">Optional id of the background task running the migration</param>
        /// <param name="throwOnErrors">If true, throw an exception when the migration completes with errors</param>
        Task ExecuteMigrationAsync(string dbMigrationOpId, string? taskId = null, bool throwOnErrors = false);

        Task<List<DbMigrationOperation>> GetLastMigrationsAsync(int page, int take);

        Task<DbMigrationOperation> GetMigrationAsync(string migrateOperationId);

        Task<DbMigrationOperation?> IsMigrationRunningAsync();

        /// <summary>
        /// Save current model changes on db.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        Task SaveChangesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Seed database context if still not seeded, applying a db migration before the seed
        /// </summary>
        /// <returns>True if seed has been executed. False otherwise</returns>
        Task<bool> SeedIfNeededAsync();

        /// <summary>
        /// Try to start a db context migration process, if no other migration is queued or running.
        /// </summary>
        /// <returns>The new migration operation, or null if another one is already in progress</returns>
        Task<DbMigrationOperation?> TryStartMigrationAsync();
    }
}
