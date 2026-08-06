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

using Etherna.MongoDB.Driver;
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
        private ILogger logger = null!;

        // Initializer.
        public void Initialize(IDbContextEngine dbContextEngine, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(dbContextEngine);
            if (IsInitialized)
                throw new InvalidOperationException("Instance already initialized");

            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

            IsInitialized = true;

            this.logger.DbMigrationManagerInitialized(dbContextEngine.Options.DbName);
        }

        // Properties.
        public bool IsInitialized { get; private set; }

        // Methods.
        public async Task ExecuteDbContextMigrationAsync(IDbContext dbContext, string dbMigrationOpId, string? taskId = null, bool throwOnErrors = false)
        {
            ArgumentNullException.ThrowIfNull(dbContext);
            ArgumentNullException.ThrowIfNull(dbMigrationOpId);
            if (dbContext.Engine.Options.IsReadOnly)
                throw new InvalidOperationException(
                    $"Can't execute a migration on the read-only db context {dbContext.Engine.Identifier}");

            var dbMigrationOp = (DbMigrationOperation)await dbContext.DbOperations.FindOneAsync(dbMigrationOpId).ConfigureAwait(false);

            // Resume the db context lock claimed with the operation at its start, keeping the
            // lease renewed while migrating, unless an outer flow (e.g. seeding) already holds one.
            /* An operation unable to resume its claim doesn't own the lock anymore: another
             * owner took it over after the lease expiration, or the claim has been released.
             * Executing would break the exclusive window, so the operation closes cancelled
             * without migrating. */
            var ambientLockLease = dbContext.Engine.DbContextLock.TryGetAmbientLease();
            var ownedLockLease = ambientLockLease is null
                ? await dbContext.Engine.DbContextLock.TryResumeClaimAsync(dbMigrationOpId).ConfigureAwait(false)
                : null;
            var lockLease = ambientLockLease ?? ownedLockLease;
            if (lockLease is null)
            {
                if (dbMigrationOp.CurrentStatus is DbMigrationOperation.Status.New or DbMigrationOperation.Status.Running)
                {
                    dbMigrationOp.TaskCancelled();
                    await dbContext.SaveChangesAsync().ConfigureAwait(false);
                }

                logger.DbMigrationCancelledWithoutLockClaim(dbMigrationOpId, dbContext.Engine.Options.DbName);

                if (throwOnErrors)
                    throw new MongodmDbMigrationException(
                        $"Error migrating {dbContext.Engine.Identifier} dbContext: operation {dbMigrationOpId} doesn't own the db context lock anymore");
                return;
            }

            /* A lost lease cancels the running steps: the exclusive window is not guaranteed
             * anymore. The operation state keeps saving without the token, to close failed. */
            var lockLostCancellation = lockLease.LeaseLostToken;

            List<Exception> errors = [];

            try
            {
                try
                {
                    // Start migrate operation.
                    dbMigrationOp.TaskStarted(taskId);
                    await dbContext.SaveChangesAsync().ConfigureAwait(false);

                    // Remove old indexes.
                    /* Read-only repositories deny index management: their indexes belong to the
                     * collection owner, so they stay out of the migration index steps.
                     * A dry run skips the index steps entirely: index management has no simulation. */
                    if (!dbMigrationOp.IsDryRun)
                    {
                        foreach (var repository in dbContext.RepositoryRegistry.Repositories.Where(r => !r.IsReadOnly))
                        {
                            dbMigrationOp.AddLog(new DeleteOldIndexesMigrationLog(
                                repository.Name,
                                MigrationLogBase.ExecutionState.Executing));
                            await dbContext.SaveChangesAsync().ConfigureAwait(false);

                            try
                            {
                                await repository.DeleteOldIndexesAsync(lockLostCancellation).ConfigureAwait(false);

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
                            },
                            dbMigrationOp.IsDryRun,
                            dbMigrationOp.IsStopAtFirstErrorEnabled,
                            lockLostCancellation).ConfigureAwait(false);

                        if (!result.Succeded)
                            errors.Add(new MongodmDbMigrationException(
                                result.TotDocumentErrors > 0
                                    ? $"Documents migration failed on \"{docMigration.SourceRepository.Name}\" repository with {result.TotDocumentErrors} document errors"
                                    : $"Documents migration failed on \"{docMigration.SourceRepository.Name}\" repository"));

                        //ended document migration log
                        dbMigrationOp.AddLog(new DocumentMigrationLog(
                            docMigration.SourceRepository.Name,
                            result.Succeded
                                ? MigrationLogBase.ExecutionState.Succeded
                                : MigrationLogBase.ExecutionState.Failed,
                            result.MigratedDocuments,
                            result.DocumentErrors,
                            result.TotDocumentErrors));

                        await dbContext.SaveChangesAsync().ConfigureAwait(false);
                    }

                    // Build new indexes.
                    //read-only repositories and dry runs stay out of the index steps, like above
                    if (!dbMigrationOp.IsDryRun)
                    {
                        foreach (var repository in dbContext.RepositoryRegistry.Repositories.Where(r => !r.IsReadOnly))
                        {
                            dbMigrationOp.AddLog(new BuildNewIndexesMigrationLog(
                                repository.Name,
                                MigrationLogBase.ExecutionState.Executing));
                            await dbContext.SaveChangesAsync().ConfigureAwait(false);

                            try
                            {
                                await repository.BuildNewIndexesAsync(lockLostCancellation).ConfigureAwait(false);

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
                    }
                }
                catch (Exception e)
                {
                    // An unhandled exception can't leave the operation on running status,
                    // or no new migration could ever start on the db context.
                    errors.Add(e);
                }

                // Complete operation.
                if (errors.Count == 0)
                    dbMigrationOp.TaskCompleted();
                else
                    dbMigrationOp.TaskFailed();

                await dbContext.SaveChangesAsync().ConfigureAwait(false);
            }
            finally
            {
                // Release the lock lease resumed by this execution, permitting new claims.
                // An ambient lease belongs to its outer flow, that releases it itself.
                if (ownedLockLease is not null)
                    await ownedLockLease.DisposeAsync().ConfigureAwait(false);
            }

            // Report errors.
            if (errors.Count > 0)
            {
                var migrationException = new MongodmDbMigrationException(
                    $"Error migrating {dbContext.Engine.Identifier} dbContext",
                    new AggregateException(errors));

                logger.DbMigrationFailed(dbMigrationOpId, dbContext.Engine.Options.DbName, migrationException);

                if (throwOnErrors)
                    throw migrationException;
            }
        }

        /*
         * Migration state reads run with exclusive access allowance,
         * so they keep working also while a migration is locking the db context.
         */
        public async Task<List<DbMigrationOperation>> GetLastMigrationsAsync(IDbContext dbContext, int page, int take)
        {
            ArgumentNullException.ThrowIfNull(dbContext);

            using var exclusiveAccess = new ExclusiveAccessHandler(dbContext.Engine.ExecutionContext);

            // Paginate on Id: ObjectId ids embed the creation instant.
            return await dbContext.DbOperations.QueryElementsAsync(elements =>
                elements.OfType<DbMigrationOperation>()
                        .Where(op => op.DbContextName == dbContext.Engine.Identifier)
                        .PaginateDescending(r => r.Id, page, take)
                        .ToListAsync()).ConfigureAwait(false);
        }

        public async Task<DbMigrationOperation> GetMigrationAsync(IDbContext dbContext, string migrateOperationId)
        {
            ArgumentNullException.ThrowIfNull(dbContext);
            ArgumentNullException.ThrowIfNull(migrateOperationId);

            using var exclusiveAccess = new ExclusiveAccessHandler(dbContext.Engine.ExecutionContext);

            var migrateOp = await dbContext.DbOperations.QueryElementsAsync(elements =>
                elements.OfType<DbMigrationOperation>()
                        .Where(op => op.Id == migrateOperationId)
                        .FirstAsync()).ConfigureAwait(false);

            return migrateOp;
        }

        public async Task<DbMigrationOperation?> IsMigrationRunningAsync(IDbContext dbContext)
        {
            ArgumentNullException.ThrowIfNull(dbContext);

            using var exclusiveAccess = new ExclusiveAccessHandler(dbContext.Engine.ExecutionContext);

            var migrateOp = await dbContext.DbOperations.QueryElementsAsync(elements =>
                elements.OfType<DbMigrationOperation>()
                        .Where(op => op.DbContextName == dbContext.Engine.Identifier)
                        .Where(op => op.CurrentStatus == DbMigrationOperation.Status.New ||
                                     op.CurrentStatus == DbMigrationOperation.Status.Running)
                        .FirstOrDefaultAsync()).ConfigureAwait(false);

            return migrateOp;
        }

        public async Task<DbMigrationOperation?> TryStartDbContextMigrationAsync(
            IDbContext dbContext,
            bool dryRun = false,
            bool stopAtFirstError = false,
            TimeSpan? lockLeaseDuration = null)
        {
            ArgumentNullException.ThrowIfNull(dbContext);

            // Deny start on a read-only db context, or with an exclusive access locking it in process.
            if (dbContext.Engine.Options.IsReadOnly ||
                dbContext.Engine.IsExclusiveWriteEnabled)
                return null;

            // Claim the db context lock with the new operation as owner.
            /* The claim is atomic on the server: with concurrent starts from any process a
             * single operation wins, and the losers delete themselves. A queued or running
             * migration (or a seeding) holds the lock, denying the start; a dead owner stops
             * renewing its lease, whose expiration unblocks new claims without manual repair.
             * The claimed lease also covers the window between here and the task execution
             * resuming it: until then nothing renews it. */
            var migrateOp = new DbMigrationOperation(dbContext.Engine, dryRun, stopAtFirstError);
            await dbContext.DbOperations.CreateAsync(migrateOp).ConfigureAwait(false);

            if (!await dbContext.Engine.DbContextLock.TryClaimAsync(migrateOp.Id, lockLeaseDuration).ConfigureAwait(false))
            {
                // Drop the operation of the denied start, reporting the denial anyway.
                /* A cleanup failure would report a failed start instead of a denied one: the
                 * operation left behind closes with the orphaned ones at the next start. */
                try
                {
                    await dbContext.DbOperations.DeleteAsync(migrateOp).ConfigureAwait(false);
                }
                catch (Exception cleanupException)
                {
                    logger.DbMigrationDeniedStartCleanupFailed(
                        migrateOp.Id, dbContext.Engine.Options.DbName, cleanupException);
                }

                return null;
            }

            /* Everything after the claim either hands the lock over to the migration task, or
             * releases it: a claim held by an operation whose task never runs would deny every
             * migration and seeding of the db context until its lease expiration. */
            try
            {
                // Close the migration operations orphaned by dead owners, directly on the server.
                /* Their statuses would misreport a migration in progress forever, while the
                 * lock lease protecting them is already expired. Server side updates tolerate
                 * the operations deleted meanwhile by concurrent losing starts. */
                var cancelledOps = await dbContext.DbOperations.UpdateManyAsync(
                    Builders<OperationBase>.Filter.OfType<DbMigrationOperation>(op =>
                        op.DbContextName == dbContext.Engine.Identifier &&
                        op.Id != migrateOp.Id &&
                        op.CurrentStatus == DbMigrationOperation.Status.New),
                    Builders<OperationBase>.Update.Set(
                        op => ((DbMigrationOperation)op).CurrentStatus,
                        DbMigrationOperation.Status.Cancelled)).ConfigureAwait(false);
                var failedOps = await dbContext.DbOperations.UpdateManyAsync(
                    Builders<OperationBase>.Filter.OfType<DbMigrationOperation>(op =>
                        op.DbContextName == dbContext.Engine.Identifier &&
                        op.Id != migrateOp.Id &&
                        op.CurrentStatus == DbMigrationOperation.Status.Running),
                    Builders<OperationBase>.Update.Set(
                        op => ((DbMigrationOperation)op).CurrentStatus,
                        DbMigrationOperation.Status.Failed)).ConfigureAwait(false);
                if (cancelledOps.ModifiedCount + failedOps.ModifiedCount > 0)
                    logger.DbMigrationClosedOrphanedOperations(
                        cancelledOps.ModifiedCount + failedOps.ModifiedCount,
                        dbContext.Engine.Options.DbName);

                taskRunner.RunMigrateDbTask(dbContext.GetType(), migrateOp.Id);
            }
            catch
            {
                // Release the claim and drop the operation, without masking the start failure.
                try
                {
                    await dbContext.Engine.DbContextLock.TryReleaseAsync(migrateOp.Id).ConfigureAwait(false);
                    await dbContext.DbOperations.DeleteAsync(migrateOp).ConfigureAwait(false);
                }
                catch (Exception cleanupException)
                {
                    logger.DbMigrationStartCleanupFailed(
                        migrateOp.Id, dbContext.Engine.Options.DbName, cleanupException);
                }

                throw;
            }

            return migrateOp;
        }
    }
}
