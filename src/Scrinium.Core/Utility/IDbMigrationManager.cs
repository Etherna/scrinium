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
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Etherna.Scrinium.Core.Utility
{
    public interface IDbMigrationManager : IDbContextEngineInitializable
    {
        /// <summary>
        /// Execute a db context migration process: delete old indexes, migrate documents, and build new indexes.
        /// Failing documents are skipped and reported into the operation logs, unless the
        /// operation asks to stop at the first error. The caller must already hold an exclusive
        /// access on the db context, except for a dry run operation, that simulates the document
        /// migrations without persisting anything and skips the index steps.
        /// The execution resumes the db context lock claimed with the operation, keeping its
        /// lease renewed and releasing it at completion, unless an outer flow (e.g. seeding)
        /// already holds a lease; an operation whose claim doesn't resume (its lock has been
        /// taken over by another owner, or released) closes cancelled without migrating.
        /// </summary>
        /// <param name="dbContext">The db context to migrate</param>
        /// <param name="dbMigrationOpId">Id of the migration operation to execute</param>
        /// <param name="taskId">Optional id of the background task running the migration</param>
        /// <param name="throwOnErrors">If true, throw an exception when the migration completes with errors</param>
        Task ExecuteDbContextMigrationAsync(IDbContext dbContext, string dbMigrationOpId, string? taskId = null, bool throwOnErrors = false);

        Task<DbMigrationOperation?> IsMigrationRunningAsync(IDbContext dbContext);

        Task<List<DbMigrationOperation>> GetLastMigrationsAsync(IDbContext dbContext, int page, int take);

        Task<DbMigrationOperation> GetMigrationAsync(IDbContext dbContext, string migrateOperationId);

        /// <summary>
        /// Try to start a db context migration process, claiming the db context lock with the
        /// new operation as owner: the claim is atomic on the server, so a single start wins
        /// also with concurrent starts from different application instances, and it is denied
        /// while another owner (a queued or running migration, or a seeding) holds the lock.
        /// Operations orphaned by dead owners close at the next start, once their lease expires.
        /// </summary>
        /// <param name="dbContext">The db context to migrate</param>
        /// <param name="dryRun">If true, start a dry run: simulate the document migrations
        /// without persisting anything, reporting failing documents into the operation logs</param>
        /// <param name="stopAtFirstError">If true, abort a documents migration at its first
        /// failing document, instead of skipping it and processing every other document</param>
        /// <param name="lockLeaseDuration">Duration of the lock lease claimed by this start,
        /// defaulted to <see cref="ResourceLock.DefaultLeaseDuration"/>: how long the db
        /// context stays locked if this application instance dies before the migration
        /// completes, and how long the claim survives waiting for the background task runner to
        /// pick the operation up, the only window nothing renews it. It doesn't have to cover
        /// the migration duration, since the execution keeps the lease renewed</param>
        /// <returns>The new migration operation, or null when the start is denied: a read-only
        /// db context, an exclusive access already running in this process, or the db context
        /// lock held by another owner</returns>
        Task<DbMigrationOperation?> TryStartDbContextMigrationAsync(
            IDbContext dbContext,
            bool dryRun = false,
            bool stopAtFirstError = false,
            TimeSpan? lockLeaseDuration = null);
    }
}