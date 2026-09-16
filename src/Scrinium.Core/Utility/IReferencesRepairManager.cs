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
using Etherna.Scrinium.Core.Options;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Etherna.Scrinium.Core.Utility
{
    /// <summary>
    /// Drives the repairs of the references pointing to missing origin documents, and their
    /// <see cref="ReferencesRepairOperation"/> log. A repair runs one collection at a time,
    /// under the db context lock, like a db migration does.
    /// </summary>
    public interface IReferencesRepairManager : IDbContextEngineInitializable
    {
        /// <summary>
        /// Execute the repair of an operation started before, closing it completed or failed.
        /// </summary>
        /// <param name="dbContext">The db context of the operation</param>
        /// <param name="repairOpId">The operation id</param>
        /// <param name="taskId">The id of the background task executing it</param>
        /// <param name="throwOnErrors">If true, rethrow what failed the operation</param>
        Task ExecuteReferencesRepairAsync(
            IDbContext dbContext,
            string repairOpId,
            string? taskId = null,
            bool throwOnErrors = false);

        /// <summary>
        /// Read a page of the references repair operations of a db context, most recent first.
        /// </summary>
        Task<List<ReferencesRepairOperation>> GetLastReferencesRepairsAsync(IDbContext dbContext, int page, int take);

        Task<ReferencesRepairOperation> GetReferencesRepairAsync(IDbContext dbContext, string repairOpId);

        /// <summary>
        /// The open references repair operation of a db context, whatever collection it
        /// repairs, or null when none is open. An operation stays open also when the instance
        /// executing it dies: pair it with a live db context lock lease to tell a repair
        /// really in progress from one orphaned by a dead owner.
        /// </summary>
        Task<ReferencesRepairOperation?> IsReferencesRepairRunningAsync(IDbContext dbContext);

        /// <summary>
        /// Start the repair of the missing origin references of one collection, claiming the
        /// db context lock and enqueuing the execution on the task runner.
        /// </summary>
        /// <param name="dbContext">The db context owning the collection</param>
        /// <param name="repositoryName">The repository whose collection is repaired</param>
        /// <param name="repairModesByElementPath">What to apply to each reference element
        /// path: the plan of the operation, rendered before it runs</param>
        /// <param name="dryRun">If true, execute it with the collection writes simulated</param>
        /// <param name="lockLeaseDuration">Duration of the lock lease claimed by this start</param>
        /// <returns>The new operation, or null when the start is denied: a read-only db
        /// context, an exclusive access already running in this process, or the db context
        /// lock held by another owner</returns>
        /// <exception cref="ArgumentException">The repository doesn't exist on the db context</exception>
        /// <exception cref="UnauthorizedAccessException">The repository is read-only</exception>
        Task<ReferencesRepairOperation?> TryStartReferencesRepairAsync(
            IDbContext dbContext,
            string repositoryName,
            IReadOnlyDictionary<string, OriginDeleteMode> repairModesByElementPath,
            bool dryRun = false,
            TimeSpan? lockLeaseDuration = null);
    }
}
