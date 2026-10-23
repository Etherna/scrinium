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
using Etherna.Scrinium.Core.Repositories;
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
            var execution = await DbOperationLifecycle.TryExecuteAsync(
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
                        var writableRepositories = dbContext.RepositoryRegistry.Repositories
                            .Where(repository => !repository.IsReadOnly)
                            .ToArray();

                        /* The steps stop at the first cancellation: a lost lease or an abort
                         * observed by a step ends the operation at that step, the following
                         * ones never start. */

                        // Remove old indexes.
                        /* Read-only repositories deny index management: their indexes belong to the
                         * collection owner, so they stay out of the migration index steps.
                         * A dry run skips the index steps entirely: index management has no simulation. */
                        if (!dbMigrationOp.IsDryRun)
                            foreach (var repository in writableRepositories)
                            {
                                if (lockLostCancellation.IsCancellationRequested)
                                    break;

                                await ExecuteIndexStepAsync(
                                    dbContext,
                                    dbMigrationOp,
                                    repository,
                                    "Old indexes deletion",
                                    new DeleteOldIndexesMigrationLog(repository.Name, MigrationLogBase.ExecutionState.Executing),
                                    repository.DeleteOldIndexesAsync,
                                    errors,
                                    lockLostCancellation).ConfigureAwait(false);
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
                                writableRepositories.Select(repository => repository.BuildDeprecatedSchemaDocumentsMigration()));

                        foreach (var docMigration in documentMigrations)
                        {
                            if (lockLostCancellation.IsCancellationRequested)
                                break;

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

                            /* A scan cancelled through the token (the lost lease, or an abort) is
                             * interrupted, not failed by its documents: its log closes interrupted
                             * with what it processed until the stop, and its error says so, the
                             * token saying why. */
                            var interrupted = lockLostCancellation.IsCancellationRequested;
                            if (!result.IsSucceeded)
                            {
                                var outcome = interrupted ? "interrupted" : "failed";
                                var failureMessage = result.TotDocumentErrors > 0
                                    ? $"Documents migration {outcome} on \"{docMigration.SourceRepository.Name}\" repository with {result.TotDocumentErrors} document errors"
                                    : $"Documents migration {outcome} on \"{docMigration.SourceRepository.Name}\" repository";

                                //the exception aborting the scan is the root cause: it travels inner, into the log and the reason
                                errors.Add(result.Exception is null
                                    ? new ScriniumDbMigrationException(failureMessage)
                                    : new ScriniumDbMigrationException(failureMessage, result.Exception));
                            }

                            //ended document migration log, replacing the rolling progress one
                            dbMigrationOp.AddDocumentMigrationLog(new DocumentMigrationLog(
                                docMigration.SourceRepository.Name,
                                result.IsSucceeded
                                    ? MigrationLogBase.ExecutionState.Succeeded
                                    : interrupted
                                        ? MigrationLogBase.ExecutionState.Interrupted
                                        : MigrationLogBase.ExecutionState.Failed,
                                result.MigratedDocuments,
                                result.DocumentErrors,
                                result.TotDocumentErrors));

                            await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                        }

                        // Build new indexes.
                        //read-only repositories and dry runs stay out of the index steps, like above
                        if (!dbMigrationOp.IsDryRun)
                            foreach (var repository in writableRepositories)
                            {
                                if (lockLostCancellation.IsCancellationRequested)
                                    break;

                                await ExecuteIndexStepAsync(
                                    dbContext,
                                    dbMigrationOp,
                                    repository,
                                    "New indexes build",
                                    new BuildNewIndexesMigrationLog(repository.Name, MigrationLogBase.ExecutionState.Executing),
                                    repository.BuildNewIndexesAsync,
                                    errors,
                                    lockLostCancellation).ConfigureAwait(false);
                            }
                    }
                    catch (Exception e)
                    {
                        /* An unhandled exception closes the operation failed, like a reported
                         * one: leaving it running would deny every new migration. */
                        errors.Add(e);
                    }

                    //the operation records every error, one line each: the aggregate exception below is for the caller
                    return errors.Count == 0
                        ? null
                        : string.Join('\n', errors.Select(DescribeFailure));

                },
                logger).ConfigureAwait(false);

            // Report errors.
            if (execution is DbOperationExecution.ClaimNotResumed or DbOperationExecution.AlreadyRunning)
            {
                if (throwOnErrors)
                    throw new ScriniumDbMigrationException(execution == DbOperationExecution.AlreadyRunning
                        ? $"Error migrating {dbContext.Engine.Identifier} dbContext: operation {dbMigrationOpId} was already running, re-executed after the worker executing it died"
                        : $"Error migrating {dbContext.Engine.Identifier} dbContext: operation {dbMigrationOpId} doesn't own the db context lock anymore");
                return;
            }

            /* An aborted operation stopped on request: what its cancelled steps reported is
             * not a failure of the operation, so nothing of it logs as one. */
            if (execution == DbOperationExecution.Aborted)
            {
                if (throwOnErrors)
                    throw new ScriniumDbMigrationException(
                        $"Error migrating {dbContext.Engine.Identifier} dbContext: operation {dbMigrationOpId} was aborted on request");
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

        public async Task<bool> TryAbortDbContextMigrationAsync(IDbContext dbContext, string dbMigrationOpId)
        {
            ArgumentNullException.ThrowIfNull(dbContext);
            ArgumentNullException.ThrowIfNull(dbMigrationOpId);

            // A read-only db context runs no migration: nothing to abort.
            if (dbContext.Engine.Options.IsReadOnly)
                return false;

            var requestDateTime = DateTimeOffset.UtcNow;
            return await DbOperationLifecycle.TryRequestAbortAsync(
                dbContext,
                dbMigrationOpId,
                Builders<OperationBase>.Filter.OfType<DbMigrationOperation>(op =>
                    op.Id == dbMigrationOpId &&
                    op.DbContextName == dbContext.Engine.Identifier &&
                    (op.CurrentStatus == DbMigrationOperation.Status.New ||
                     op.CurrentStatus == DbMigrationOperation.Status.Running) &&
                    op.AbortRequestedDateTime == null),
                Builders<OperationBase>.Update.Set(
                    op => ((DbMigrationOperation)op).AbortRequestedDateTime,
                    requestDateTime),
                logger).ConfigureAwait(false);
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

        // Helpers.
        /* An index step logs once: its log opens executing and closes in place with the end of
         * the step. A step cancelled through the token is interrupted, not failed: its error
         * says so, the token saying why; any other failure keeps its step in the reason, the
         * cause travelling inner. */
        private static async Task ExecuteIndexStepAsync(
            IDbContext dbContext,
            DbMigrationOperation operation,
            IRepository repository,
            string stepName,
            MigrationLogBase log,
            Func<CancellationToken, Task> stepAsync,
            List<Exception> errors,
            CancellationToken cancellationToken)
        {
            operation.AddLog(log);
            await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

            try
            {
                await stepAsync(cancellationToken).ConfigureAwait(false);

                log.Close(MigrationLogBase.ExecutionState.Succeeded);
            }
            catch (Exception e)
            {
                var interrupted = cancellationToken.IsCancellationRequested;
                errors.Add(new ScriniumDbMigrationException(
                    $"{stepName} {(interrupted ? "interrupted" : "failed")} on \"{repository.Name}\" repository", e));

                log.Close(interrupted
                    ? MigrationLogBase.ExecutionState.Interrupted
                    : MigrationLogBase.ExecutionState.Failed);
            }

            await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /* A migration exception of this manager carries its context in the message, with the
         * cause inner: the reason reads as the operator needs it, context first, then the type
         * and message of the exception that failed the step. */
        private static string DescribeFailure(Exception e) => e switch
        {
            ScriniumDbMigrationException { InnerException: { } cause } => $"{e.Message}: {DescribeFailure(cause)}",
            ScriniumDbMigrationException => e.Message,
            _ => $"{e.GetType().Name}: {e.Message}"
        };
    }
}
