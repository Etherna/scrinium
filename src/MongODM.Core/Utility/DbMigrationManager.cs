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

using Etherna.MongoDB.Driver.Linq;
using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.Domain.Models.DbMigrationOpAgg;
using Etherna.MongODM.Core.Exceptions;
using Etherna.MongODM.Core.Extensions;
using Etherna.MongODM.Core.Tasks;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core.Utility
{
    public class DbMigrationManager(ITaskRunner taskRunner) : IDbMigrationManager
    {
        // Fields.
        private IDbContext dbContext = null!;
        private ILogger logger = null!;

        // Initializer.
        public void Initialize(IDbContext dbContext, ILogger logger)
        {
            if (IsInitialized)
                throw new InvalidOperationException("Instance already initialized");

            this.dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

            IsInitialized = true;

            this.logger.DbMigrationManagerInitialized(dbContext.Options.DbName);
        }

        // Properties.
        public bool IsInitialized { get; private set; }

        // Methods.
        public async Task ExecuteDbContextMigrationAsync(string dbMigrationOpId, string? taskId = null, bool throwOnErrors = false)
        {
            ArgumentNullException.ThrowIfNull(dbMigrationOpId);

            var dbMigrationOp = (DbMigrationOperation)await dbContext.DbOperations.FindOneAsync(dbMigrationOpId).ConfigureAwait(false);
            List<Exception> errors = [];

            // Start migrate operation.
            dbMigrationOp.TaskStarted(taskId);
            await dbContext.SaveChangesAsync().ConfigureAwait(false);

            // Remove old indexes.
            foreach (var repository in dbContext.RepositoryRegistry.Repositories)
            {
                dbMigrationOp.AddLog(new DeleteOldIndexesMigrationLog(
                    repository.Name,
                    MigrationLogBase.ExecutionState.Executing));
                await dbContext.SaveChangesAsync().ConfigureAwait(false);

                try
                {
                    await repository.DeleteOldIndexesAsync().ConfigureAwait(false);

                    dbMigrationOp.AddLog(new DeleteOldIndexesMigrationLog(
                        repository.Name,
                        MigrationLogBase.ExecutionState.Succeded));
                }
                catch (Exception e)
                {
                    errors.Add(e);

                    dbMigrationOp.AddLog(new DeleteOldIndexesMigrationLog(
                        repository.Name,
                        MigrationLogBase.ExecutionState.Failed));
                }

                await dbContext.SaveChangesAsync().ConfigureAwait(false);
            }

            // Migrate documents.
            foreach (var docMigration in dbContext.DocumentMigrationList)
            {
                //running document migration
                var result = await docMigration.MigrateAsync(500,
                    async procDocs =>
                    {
                        dbMigrationOp.AddLog(new DocumentMigrationLog(
                            docMigration.SourceRepository.Name,
                            MigrationLogBase.ExecutionState.Executing,
                            procDocs));

                        await dbContext.SaveChangesAsync().ConfigureAwait(false);
                    }).ConfigureAwait(false);

                if (!result.Succeded)
                    errors.Add(new MongodmDbMigrationException(
                        $"Documents migration failed on \"{docMigration.SourceRepository.Name}\" repository"));

                //ended document migration log
                dbMigrationOp.AddLog(new DocumentMigrationLog(
                    docMigration.SourceRepository.Name,
                    result.Succeded
                        ? MigrationLogBase.ExecutionState.Succeded
                        : MigrationLogBase.ExecutionState.Failed,
                    result.MigratedDocuments));

                await dbContext.SaveChangesAsync().ConfigureAwait(false);
            }

            // Build new indexes.
            foreach (var repository in dbContext.RepositoryRegistry.Repositories)
            {
                dbMigrationOp.AddLog(new BuildNewIndexesMigrationLog(
                    repository.Name,
                    MigrationLogBase.ExecutionState.Executing));
                await dbContext.SaveChangesAsync().ConfigureAwait(false);

                try
                {
                    await repository.BuildNewIndexesAsync().ConfigureAwait(false);

                    dbMigrationOp.AddLog(new BuildNewIndexesMigrationLog(
                        repository.Name,
                        MigrationLogBase.ExecutionState.Succeded));
                }
                catch (Exception e)
                {
                    errors.Add(e);

                    dbMigrationOp.AddLog(new BuildNewIndexesMigrationLog(
                        repository.Name,
                        MigrationLogBase.ExecutionState.Failed));
                }

                await dbContext.SaveChangesAsync().ConfigureAwait(false);
            }

            // Complete operation.
            if (errors.Count == 0)
                dbMigrationOp.TaskCompleted();
            else
                dbMigrationOp.TaskFailed();

            await dbContext.SaveChangesAsync().ConfigureAwait(false);

            // Report errors if required.
            if (errors.Count > 0 && throwOnErrors)
                throw new MongodmDbMigrationException(
                    $"Error migrating {dbContext.Identifier} dbContext",
                    new AggregateException(errors));
        }

        /*
         * Migration state reads run with exclusive access allowance,
         * so they keep working also while a migration is locking the db context.
         */
        public async Task<List<DbMigrationOperation>> GetLastMigrationsAsync(int page, int take)
        {
            using var exclusiveAccess = new ExclusiveAccessHandler(dbContext.ExecutionContext);

            // Paginate on Id: CreationDateTime is not persisted, and ObjectId ids embed the creation instant.
            return await dbContext.DbOperations.QueryElementsAsync(elements =>
                elements.OfType<DbMigrationOperation>()
                        .Where(op => op.DbContextName == dbContext.Identifier)
                        .PaginateDescending(r => r.Id, page, take)
                        .ToListAsync()).ConfigureAwait(false);
        }

        public async Task<DbMigrationOperation> GetMigrationAsync(string migrateOperationId)
        {
            ArgumentNullException.ThrowIfNull(migrateOperationId);

            using var exclusiveAccess = new ExclusiveAccessHandler(dbContext.ExecutionContext);

            var migrateOp = await dbContext.DbOperations.QueryElementsAsync(elements =>
                elements.OfType<DbMigrationOperation>()
                        .Where(op => op.Id == migrateOperationId)
                        .FirstAsync()).ConfigureAwait(false);

            return migrateOp;
        }

        public async Task<DbMigrationOperation?> IsMigrationRunningAsync()
        {
            using var exclusiveAccess = new ExclusiveAccessHandler(dbContext.ExecutionContext);

            var migrateOp = await dbContext.DbOperations.QueryElementsAsync(elements =>
                elements.OfType<DbMigrationOperation>()
                        .Where(op => op.DbContextName == dbContext.Identifier)
                        .Where(op => op.CurrentStatus == DbMigrationOperation.Status.New ||
                                     op.CurrentStatus == DbMigrationOperation.Status.Running)
                        .FirstOrDefaultAsync()).ConfigureAwait(false);

            return migrateOp;
        }

        public async Task<DbMigrationOperation?> TryStartDbContextMigrationAsync()
        {
            // Deny start when another migration is queued or running, or an exclusive access is locking the db context.
            if (dbContext.IsExclusiveWriteEnabled ||
                await IsMigrationRunningAsync().ConfigureAwait(false) is not null)
                return null;

            var migrateOp = new DbMigrationOperation(dbContext);
            await dbContext.DbOperations.CreateAsync(migrateOp).ConfigureAwait(false);

            taskRunner.RunMigrateDbTask(dbContext.GetType(), migrateOp.Id);

            return migrateOp;
        }
    }
}
