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
using System;
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
        /// List of models with pending changes to save, registered by change auditing
        /// on this db context instance.
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
        /// Register a model into the changed models list of this db context instance.
        /// Invoked by change auditing at the first change of a bound model.
        /// </summary>
        /// <param name="model">The changed model</param>
        void RegisterChangedModel(IEntityModel model);

        /// <summary>
        /// Register a model instance as the loaded one for its document on this db context
        /// instance. Following loads of the same document will return the same instance.
        /// </summary>
        /// <param name="modelId">The model document id</param>
        /// <param name="model">The loaded model instance</param>
        void RegisterLoadedModel(object modelId, IEntityModel model);

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
        /// Try to get the model instance already loaded on this db context instance for a
        /// document. The model type is resolved to the root type handled by its repository.
        /// </summary>
        /// <param name="modelType">The model type</param>
        /// <param name="modelId">The model document id</param>
        /// <returns>The loaded model instance, or null when absent</returns>
        IEntityModel? TryGetLoadedModel(Type modelType, object modelId);

        /// <summary>
        /// Try to start a db context migration process, if no other migration is queued or running.
        /// </summary>
        /// <returns>The new migration operation, or null if another one is already in progress</returns>
        Task<DbMigrationOperation?> TryStartMigrationAsync();

        /// <summary>
        /// Remove a model from the changed models list of this db context instance,
        /// keeping it out of the next changes save.
        /// </summary>
        /// <param name="model">The model to remove</param>
        void UnregisterChangedModel(IEntityModel model);

        /// <summary>
        /// Remove a model instance from the loaded models of this db context instance,
        /// keeping it out of next loads deduplication.
        /// </summary>
        /// <param name="modelId">The model document id</param>
        /// <param name="model">The model instance to remove</param>
        void UnregisterLoadedModel(object modelId, IEntityModel model);
    }
}
