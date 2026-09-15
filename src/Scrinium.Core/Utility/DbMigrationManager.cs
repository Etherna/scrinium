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

using Etherna.MongoDB.Driver;
using Etherna.MongoDB.Driver.Linq;
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Domain.Models.DbMigrationOpAgg;
using Etherna.Scrinium.Core.Exceptions;
using Etherna.Scrinium.Core.Extensions;
using Etherna.Scrinium.Core.Tasks;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.Scrinium.Core.Utility
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

            List<Exception> errors = [];
            var executed = await DbOperationLifecycle.TryExecuteAsync(
                dbContext,
                dbMigrationOpId,
                dbMigrationOp,
                taskId,
                /* The operation state saves without the lock lost token: an operation losing
                 * its lease still has to record what it did and close failed. */
                async lockLostCancellation =>
                {
                    try
                    {
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
                                await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

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

                                await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                            }
                        }

                        // Migrate documents.
                        /* The document migrations the application declares, and then, when the
                         * operation asks for it, the rewrite of the documents left on a deprecated
                         * schema on every writable collection: one loop, so both report through the
                         * same logs and honor the same dry run and stop at first error. Read-only
                         * repositories stay out of the rewrite, like they stay out of the index
                         * steps: their documents belong to the collection owner. */
                        var documentMigrations = dbContext.DocumentMigrationList;
                        if (dbMigrationOp.IsDeprecatedSchemaRewriteEnabled)
                            documentMigrations = documentMigrations.Concat(
                                dbContext.RepositoryRegistry.Repositories
                                    .Where(repository => !repository.IsReadOnly)
                                    .Select(repository => repository.BuildDeprecatedSchemaDocumentsMigration()));

                        foreach (var docMigration in documentMigrations)
                        {
                            //running document migration, reporting progress on a single rolling log
                            var result = await docMigration.MigrateAsync(
                                dbContext.Engine.Options.MigrationCallbackEveryTotDocuments,
                                async procDocs =>
                                {
                                    dbMigrationOp.AddDocumentMigrationLog(new DocumentMigrationLog(
                                        docMigration.SourceRepository.Name,
                                        MigrationLogBase.ExecutionState.Executing,
                                        procDocs));

                                    await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                                },
                                dbMigrationOp.IsDryRun,
                                dbMigrationOp.IsStopAtFirstErrorEnabled,
                                dbContext.Engine.Options.MigrationEvictEveryTotDocuments,
                                lockLostCancellation).ConfigureAwait(false);

                            if (!result.Succeded)
                                errors.Add(new ScriniumDbMigrationException(
                                    result.TotDocumentErrors > 0
                                        ? $"Documents migration failed on \"{docMigration.SourceRepository.Name}\" repository with {result.TotDocumentErrors} document errors"
                                        : $"Documents migration failed on \"{docMigration.SourceRepository.Name}\" repository"));

                            //ended document migration log, replacing the rolling progress one
                            dbMigrationOp.AddDocumentMigrationLog(new DocumentMigrationLog(
                                docMigration.SourceRepository.Name,
                                result.Succeded
                                    ? MigrationLogBase.ExecutionState.Succeded
                                    : MigrationLogBase.ExecutionState.Failed,
                                result.MigratedDocuments,
                                result.DocumentErrors,
                                result.TotDocumentErrors));

                            await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
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
                                await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

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

                                await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        /* An unhandled exception closes the operation failed, like a reported
                         * one: leaving it running would deny every new migration. */
                        errors.Add(e);
                    }

                    return errors.Count == 0;
                },
                logger).ConfigureAwait(false);

            // Report errors.
            if (!executed)
            {
                if (throwOnErrors)
                    throw new ScriniumDbMigrationException(
                        $"Error migrating {dbContext.Engine.Identifier} dbContext: operation {dbMigrationOpId} doesn't own the db context lock anymore");
                return;
            }

            if (errors.Count > 0)
            {
                var migrationException = new ScriniumDbMigrationException(
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

            using var exclusiveAccess = new ExclusiveAccessHandler(dbContext.Engine);

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

            using var exclusiveAccess = new ExclusiveAccessHandler(dbContext.Engine);

            var migrateOp = await dbContext.DbOperations.QueryElementsAsync(elements =>
                elements.OfType<DbMigrationOperation>()
                        .Where(op => op.Id == migrateOperationId)
                        .FirstAsync()).ConfigureAwait(false);

            return migrateOp;
        }

        public async Task<DbMigrationOperation?> IsMigrationRunningAsync(IDbContext dbContext)
        {
            ArgumentNullException.ThrowIfNull(dbContext);

            using var exclusiveAccess = new ExclusiveAccessHandler(dbContext.Engine);

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
            TimeSpan? lockLeaseDuration = null,
            bool rewriteDeprecatedSchemas = false)
        {
            ArgumentNullException.ThrowIfNull(dbContext);

            // Deny start on a read-only db context, or with an exclusive access locking it in process.
            if (dbContext.Engine.Options.IsReadOnly ||
                dbContext.Engine.IsExclusiveWriteEnabled)
                return null;

            var migrateOp = new DbMigrationOperation(dbContext.Engine, dryRun, stopAtFirstError, rewriteDeprecatedSchemas);

            var started = await DbOperationLifecycle.TryStartAsync(
                dbContext,
                migrateOp,
                lockLeaseDuration,
                () => taskRunner.RunMigrateDbTask(dbContext.GetType(), migrateOp.Id),
                logger).ConfigureAwait(false);

            return started ? migrateOp : null;
        }

    }
}
