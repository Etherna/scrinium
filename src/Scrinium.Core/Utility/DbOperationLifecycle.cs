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

using Etherna.MongoDB.Bson;
using Etherna.MongoDB.Driver;
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Domain.Models.DbMigrationOpAgg;
using Etherna.Scrinium.Core.Domain.Models.ReferencesRepairOpAgg;
using Etherna.Scrinium.Core.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.Scrinium.Core.Utility
{
    /// <summary>
    /// The lifecycle every db operation executed in background shares: claiming the db context
    /// lock with the operation as owner, handing it over to the background task, resuming the
    /// claim inside the task with its lease renewed, and closing the operation whatever the
    /// body does. Only the work between the transitions belongs to each kind of operation.
    /// </summary>
    internal static class DbOperationLifecycle
    {
        // Consts.
        internal const string LostLeaseFailureReason =
            "The db context lock lease was lost while the operation was running";
        internal const string ReExecutionFailureReason =
            "The operation was still running when its execution resumed the db context lock claim: re-executed after the worker executing it died";

        /// <summary>
        /// Claim the db context lock with a new operation as owner, and hand it over to the
        /// background task. The claim is atomic on the server: with concurrent starts from any
        /// process a single operation wins, and the losers delete themselves.
        /// </summary>
        /// <returns>True when the operation started, false when the claim was denied</returns>
        public static async Task<bool> TryStartAsync<TOperation>(
            IDbContext dbContext,
            TOperation operation,
            TimeSpan? lockLeaseDuration,
            Action enqueueTask,
            ILogger logger)
            where TOperation : OperationBase, IRunnableOperation
        {
            await dbContext.DbOperations.CreateAsync(operation).ConfigureAwait(false);

            /* A queued or running operation (or a seeding) holds the lock, denying the start;
             * a dead owner stops renewing its lease, whose expiration unblocks new claims
             * without manual repair. The claimed lease also covers the window between here and
             * the task execution resuming it: until then nothing renews it. */
            if (!await dbContext.Engine.DbContextLock.TryClaimAsync(operation.Id, lockLeaseDuration).ConfigureAwait(false))
            {
                // Drop the operation of the denied start, reporting the denial anyway.
                /* A cleanup failure would report a failed start instead of a denied one: the
                 * operation left behind closes with the orphaned ones at the next start. */
                try
                {
                    await dbContext.DbOperations.DeleteAsync(operation).ConfigureAwait(false);
                }
                catch (Exception cleanupException)
                {
                    logger.DbOperationDeniedStartCleanupFailed(
                        operation.Id, dbContext.Engine.Options.DbName, cleanupException);
                }

                return false;
            }

            /* Everything after the claim either hands the lock over to the task, or releases
             * it: a claim held by an operation whose task never runs would deny every
             * operation and seeding of the db context until its lease expiration. */
            try
            {
                await CloseOrphanedOperationsAsync(dbContext, operation.Id, logger).ConfigureAwait(false);
                enqueueTask();
            }
            catch
            {
                // Release the claim and drop the operation, without masking the start failure.
                try
                {
                    await dbContext.Engine.DbContextLock.TryReleaseAsync(operation.Id).ConfigureAwait(false);
                    await dbContext.DbOperations.DeleteAsync(operation).ConfigureAwait(false);
                }
                catch (Exception cleanupException)
                {
                    logger.DbOperationStartCleanupFailed(
                        operation.Id, dbContext.Engine.Options.DbName, cleanupException);
                }

                throw;
            }

            return true;
        }

        /// <summary>
        /// Close the operations orphaned by dead owners, directly on the server, whatever
        /// kind they are: this runs right after a successful claim, and a claim only succeeds
        /// when nobody live holds the lock — so every other operation still open is orphaned,
        /// and its status would misreport a work in progress forever. The kinds share the db
        /// context lock, so one kind sweeping only its own would leave the others reported as
        /// running by the liveness check, which reads whether the lock is held, not by whom.
        /// </summary>
        private static async Task CloseOrphanedOperationsAsync(
            IDbContext dbContext,
            string startingOperationId,
            ILogger logger)
        {
            /* Server side updates tolerate the operations deleted meanwhile by concurrent
             * losing starts. The swept operations close with their instant and, the running
             * ones, with the sweep as their failure reason: the document is all an operator
             * reads of an operation whose owner died. */
            var closedOperations = 0L;
            var closingDateTime = DateTimeOffset.UtcNow;
            var orphanFailureReason =
                $"Closed as orphaned by the start of operation {startingOperationId}: " +
                "its owner stopped renewing the db context lock lease";

            closedOperations += await CloseAsync(
                Builders<OperationBase>.Filter.OfType<DbMigrationOperation>(op =>
                    op.DbContextName == dbContext.Engine.Identifier &&
                    op.Id != startingOperationId &&
                    op.CurrentStatus == DbMigrationOperation.Status.New),
                Builders<OperationBase>.Update
                    .Set(op => ((DbMigrationOperation)op).CurrentStatus, DbMigrationOperation.Status.Cancelled)
                    .Set(op => ((DbMigrationOperation)op).CompletedDateTime, closingDateTime)).ConfigureAwait(false);
            /* The executing logs and path states of a swept operation close interrupted in the
             * same update, through an array filter on their state: the swept document reads
             * consistently, whatever the dead owner was doing. The element and state names are
             * the stored ones: the execution states are stored as strings, by the enum
             * representation convention registered with the library. */
            closedOperations += await CloseAsync(
                Builders<OperationBase>.Filter.OfType<DbMigrationOperation>(op =>
                    op.DbContextName == dbContext.Engine.Identifier &&
                    op.Id != startingOperationId &&
                    op.CurrentStatus == DbMigrationOperation.Status.Running),
                Builders<OperationBase>.Update
                    .Set(op => ((DbMigrationOperation)op).CurrentStatus, DbMigrationOperation.Status.Failed)
                    .Set(op => ((DbMigrationOperation)op).CompletedDateTime, closingDateTime)
                    .Set(op => ((DbMigrationOperation)op).FailureReason, orphanFailureReason)
                    .Set(
                        $"{nameof(DbMigrationOperation.Logs)}.$[executingLog].{nameof(MigrationLogBase.State)}",
                        nameof(MigrationLogBase.ExecutionState.Interrupted)),
                new UpdateOptions
                {
                    ArrayFilters =
                    [
                        new BsonDocumentArrayFilterDefinition<BsonDocument>(new BsonDocument(
                            $"executingLog.{nameof(MigrationLogBase.State)}",
                            nameof(MigrationLogBase.ExecutionState.Executing)))
                    ]
                }).ConfigureAwait(false);
            closedOperations += await CloseAsync(
                Builders<OperationBase>.Filter.OfType<ReferencesRepairOperation>(op =>
                    op.DbContextName == dbContext.Engine.Identifier &&
                    op.Id != startingOperationId &&
                    op.CurrentStatus == ReferencesRepairOperation.Status.New),
                Builders<OperationBase>.Update
                    .Set(op => ((ReferencesRepairOperation)op).CurrentStatus, ReferencesRepairOperation.Status.Cancelled)
                    .Set(op => ((ReferencesRepairOperation)op).CompletedDateTime, closingDateTime)).ConfigureAwait(false);
            closedOperations += await CloseAsync(
                Builders<OperationBase>.Filter.OfType<ReferencesRepairOperation>(op =>
                    op.DbContextName == dbContext.Engine.Identifier &&
                    op.Id != startingOperationId &&
                    op.CurrentStatus == ReferencesRepairOperation.Status.Running),
                Builders<OperationBase>.Update
                    .Set(op => ((ReferencesRepairOperation)op).CurrentStatus, ReferencesRepairOperation.Status.Failed)
                    .Set(op => ((ReferencesRepairOperation)op).CompletedDateTime, closingDateTime)
                    .Set(op => ((ReferencesRepairOperation)op).FailureReason, orphanFailureReason)
                    .Set(
                        $"{nameof(ReferencesRepairOperation.PathStates)}.$[executingPath].{nameof(ReferencesRepairPathState.State)}",
                        nameof(ReferencesRepairPathState.ExecutionState.Interrupted)),
                new UpdateOptions
                {
                    ArrayFilters =
                    [
                        new BsonDocumentArrayFilterDefinition<BsonDocument>(new BsonDocument(
                            $"executingPath.{nameof(ReferencesRepairPathState.State)}",
                            nameof(ReferencesRepairPathState.ExecutionState.Executing)))
                    ]
                }).ConfigureAwait(false);

            if (closedOperations > 0)
                logger.DbOperationClosedOrphanedOperations(closedOperations, dbContext.Engine.Options.DbName);

            async Task<long> CloseAsync(
                FilterDefinition<OperationBase> filter,
                UpdateDefinition<OperationBase> update,
                UpdateOptions? options = null) =>
                (await dbContext.DbOperations.UpdateManyAsync(filter, update, options).ConfigureAwait(false)).ModifiedCount;
        }

        /// <summary>
        /// Execute an operation under the db context lock claimed by its start, closing it
        /// completed or failed. An execution unable to resume that claim doesn't own the lock
        /// anymore — another owner took it over after the lease expiration, or the claim has
        /// been released — so it closes the operation cancelled without doing anything. An
        /// execution finding the operation already running is the task runner re-executing
        /// the job of a worker that died with the operation in progress: nothing of that run
        /// resumes, so it closes the operation failed, its executing logs interrupted, without
        /// doing anything and without failing the job.
        /// </summary>
        /// <param name="dbContext">The db context of the operation</param>
        /// <param name="operationId">The id the start claimed the db context lock with: the
        /// claim to resume, named by the caller rather than read back from the operation</param>
        /// <param name="operation">The operation to execute, already persisted by its start</param>
        /// <param name="taskId">The id of the background task executing it</param>
        /// <param name="executeAsync">The work of the operation, receiving the token cancelled
        /// when the lock lease is lost — the exclusive window is not guaranteed anymore — and
        /// returning null when the operation completed without errors, or the reason failing
        /// it, recorded on the operation</param>
        /// <param name="logger">The db context logger</param>
        /// <returns>Whether the body ran, or why it didn't</returns>
        public static async Task<DbOperationExecution> TryExecuteAsync<TOperation>(
            IDbContext dbContext,
            string operationId,
            TOperation operation,
            string? taskId,
            Func<CancellationToken, Task<string?>> executeAsync,
            ILogger logger)
            where TOperation : OperationBase, IRunnableOperation
        {
            ArgumentNullException.ThrowIfNull(executeAsync);

            // Resume the claim of the start, unless an outer flow (e.g. seeding) holds one.
            var ambientLockLease = dbContext.Engine.DbContextLock.TryGetAmbientLease();
            var ownedLockLease = ambientLockLease is null
                ? await dbContext.Engine.DbContextLock.TryResumeClaimAsync(operationId).ConfigureAwait(false)
                : null;
            var lockLease = ambientLockLease ?? ownedLockLease;
            if (lockLease is null)
            {
                if (operation.IsOpen)
                {
                    operation.TaskCancelled();
                    await dbContext.SaveChangesAsync().ConfigureAwait(false);
                }

                logger.DbOperationCancelledWithoutLockClaim(operationId, dbContext.Engine.Options.DbName);

                return DbOperationExecution.ClaimNotResumed;
            }

            try
            {
                /* The task runner re-executes the job of a worker that died with the operation
                 * in progress (its invisibility timeout re-queues what the dead worker had
                 * fetched): the operation is still running when the claim resumes. Nothing of
                 * that run resumes, so the operation closes failed, and the job completes
                 * instead of failing on the denied start, which the runner would only retry. */
                if (operation.IsRunning)
                {
                    operation.TaskFailed(ReExecutionFailureReason);
                    await dbContext.SaveChangesAsync().ConfigureAwait(false);

                    logger.DbOperationFailedOnReExecution(operationId, dbContext.Engine.Options.DbName);

                    return DbOperationExecution.AlreadyRunning;
                }

                string? failureReason;
                try
                {
                    operation.TaskStarted(taskId);
                    await dbContext.SaveChangesAsync().ConfigureAwait(false);

                    /* A lost lease cancels the running work: the exclusive window is not
                     * guaranteed anymore. The operation state keeps saving without the token,
                     * to close failed. */
                    failureReason = await executeAsync(lockLease.LeaseLostToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    /* An unhandled exception can't leave the operation on running status, or
                     * no new operation could ever start on the db context. The operation
                     * records it: the exception itself reaches the task runner alone. */
                    operation.TaskFailed($"{e.GetType().Name}: {e.Message}");
                    await dbContext.SaveChangesAsync().ConfigureAwait(false);
                    throw;
                }

                /* The body reports a lost lease as the cancellation it observed: the lease is
                 * the cause, so the operation records it first, with what the body reported. */
                if (lockLease.LeaseLostToken.IsCancellationRequested)
                    failureReason = failureReason is null
                        ? LostLeaseFailureReason
                        : $"{LostLeaseFailureReason}: {failureReason}";

                if (failureReason is null)
                    operation.TaskCompleted();
                else
                    operation.TaskFailed(failureReason);

                await dbContext.SaveChangesAsync().ConfigureAwait(false);
            }
            finally
            {
                // Release the lease resumed by this execution, permitting new claims.
                // An ambient lease belongs to its outer flow, that releases it itself.
                if (ownedLockLease is not null)
                    await ownedLockLease.DisposeAsync().ConfigureAwait(false);
            }

            return DbOperationExecution.Executed;
        }
    }
}
