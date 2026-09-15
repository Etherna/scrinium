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

using Etherna.MongoDB.Driver.Linq;
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Exceptions;
using Etherna.Scrinium.Core.Extensions;
using Etherna.Scrinium.Core.Options;
using Etherna.Scrinium.Core.Tasks;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.Scrinium.Core.Utility
{
    public class ReferencesRepairManager(ITaskRunner taskRunner) : IReferencesRepairManager
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
        }

        // Properties.
        public bool IsInitialized { get; private set; }

        // Methods.
        public async Task ExecuteReferencesRepairAsync(
            IDbContext dbContext,
            string repairOpId,
            string? taskId = null,
            bool throwOnErrors = false)
        {
            ArgumentNullException.ThrowIfNull(dbContext);
            ArgumentNullException.ThrowIfNull(repairOpId);
            if (dbContext.Engine.Options.IsReadOnly)
                throw new InvalidOperationException(
                    $"Can't repair references on the read-only db context {dbContext.Engine.Identifier}");

            var repairOp = (ReferencesRepairOperation)await dbContext.DbOperations.FindOneAsync(repairOpId).ConfigureAwait(false);

            Exception? error = null;
            var executed = await DbOperationLifecycle.TryExecuteAsync(
                dbContext,
                repairOpId,
                repairOp,
                taskId,
                /* The operation state saves without the lock lost token: an operation losing
                 * its lease still has to record what it did and close failed. */
                async lockLostCancellation =>
                {
                    try
                    {
                        var repository = dbContext.RepositoryRegistry.Repositories
                            .FirstOrDefault(repo => repo.Name == repairOp.RepositoryName)
                            ?? throw new ScriniumReferencesRepairException(
                                $"Repository \"{repairOp.RepositoryName}\" doesn't exist on db context {dbContext.Engine.Identifier}");

                        var repairModes = repairOp.PathStates.ToDictionary(
                            pathState => pathState.ElementPath,
                            pathState => pathState.RepairMode,
                            StringComparer.Ordinal);

                        repairOp.ReportPathsStarted();
                        await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

                        var report = await repository.RepairMissingOriginReferencesAsync(
                            repairModes,
                            repairOp.IsDryRun,
                            /* Each path reports what it brought so far while it runs: the
                             * dashboard renders the counters growing, and an interrupted
                             * operation keeps what it reported. */
                            async pathRepair =>
                            {
                                repairOp.ReportPathProgress(
                                    pathRepair.ElementPath,
                                    pathRepair.MissingOriginIdsCount,
                                    pathRepair.UpdatedDocumentsCount,
                                    pathRepair.DeletedDocumentsCount);

                                await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                            },
                            lockLostCancellation).ConfigureAwait(false);

                        foreach (var pathRepair in report.PathRepairs)
                        {
                            //a path its mode keeps was never scanned: it reports skipped, not done
                            if (pathRepair.RepairMode == OriginDeleteMode.KeepReference)
                                repairOp.ReportPathSkipped(pathRepair.ElementPath);
                            else
                                repairOp.ReportPathEnded(
                                    pathRepair.ElementPath,
                                    pathRepair.MissingOriginIdsCount,
                                    pathRepair.UpdatedDocumentsCount,
                                    pathRepair.DeletedDocumentsCount);
                        }

                        await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        /* The repair fails whole: it reads the collection references path by
                         * path, so what didn't complete stays as it was. Every path still
                         * running records the error that stopped it. */
                        error = e;

                        repairOp.FailOpenPaths($"{e.GetType().Name}: {e.Message}");

                        await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                    }

                    return error is null;
                },
                logger).ConfigureAwait(false);

            if (!executed)
            {
                if (throwOnErrors)
                    throw new ScriniumReferencesRepairException(
                        $"Error repairing references of {dbContext.Engine.Identifier} dbContext: operation {repairOpId} doesn't own the db context lock anymore");
                return;
            }

            if (error is not null)
            {
                logger.ReferencesRepairFailed(repairOpId, dbContext.Engine.Options.DbName, error);

                if (throwOnErrors)
                    throw error;
            }
        }

        /*
         * Repair state reads run with exclusive access allowance,
         * so they keep working also while an operation is locking the db context.
         */
        public async Task<List<ReferencesRepairOperation>> GetLastReferencesRepairsAsync(IDbContext dbContext, int page, int take)
        {
            ArgumentNullException.ThrowIfNull(dbContext);

            using var exclusiveAccess = new ExclusiveAccessHandler(dbContext.Engine);

            // Paginate on Id: ObjectId ids embed the creation instant.
            return await dbContext.DbOperations.QueryElementsAsync(elements =>
                elements.OfType<ReferencesRepairOperation>()
                        .Where(op => op.DbContextName == dbContext.Engine.Identifier)
                        .PaginateDescending(r => r.Id, page, take)
                        .ToListAsync()).ConfigureAwait(false);
        }

        public async Task<ReferencesRepairOperation> GetReferencesRepairAsync(IDbContext dbContext, string repairOpId)
        {
            ArgumentNullException.ThrowIfNull(dbContext);
            ArgumentNullException.ThrowIfNull(repairOpId);

            using var exclusiveAccess = new ExclusiveAccessHandler(dbContext.Engine);

            return await dbContext.DbOperations.QueryElementsAsync(elements =>
                elements.OfType<ReferencesRepairOperation>()
                        .Where(op => op.Id == repairOpId)
                        .FirstAsync()).ConfigureAwait(false);
        }

        public async Task<ReferencesRepairOperation?> IsReferencesRepairRunningAsync(IDbContext dbContext)
        {
            ArgumentNullException.ThrowIfNull(dbContext);

            using var exclusiveAccess = new ExclusiveAccessHandler(dbContext.Engine);

            return await dbContext.DbOperations.QueryElementsAsync(elements =>
                elements.OfType<ReferencesRepairOperation>()
                        .Where(op => op.DbContextName == dbContext.Engine.Identifier)
                        .Where(op => op.CurrentStatus == ReferencesRepairOperation.Status.New ||
                                     op.CurrentStatus == ReferencesRepairOperation.Status.Running)
                        .FirstOrDefaultAsync()).ConfigureAwait(false);
        }

        public async Task<ReferencesRepairOperation?> TryStartReferencesRepairAsync(
            IDbContext dbContext,
            string repositoryName,
            IReadOnlyDictionary<string, OriginDeleteMode> repairModesByElementPath,
            bool dryRun = false,
            TimeSpan? lockLeaseDuration = null)
        {
            ArgumentNullException.ThrowIfNull(dbContext);
            ArgumentNullException.ThrowIfNull(repairModesByElementPath);

            // Deny start on a read-only db context, or with an exclusive access locking it in process.
            if (dbContext.Engine.Options.IsReadOnly ||
                dbContext.Engine.IsExclusiveWriteEnabled)
                return null;

            /* A repair writes on the collection: refuse a repository that doesn't exist, and a
             * read-only one, before claiming anything. */
            var repository = dbContext.RepositoryRegistry.Repositories
                .FirstOrDefault(repo => repo.Name == repositoryName)
                ?? throw new ArgumentException(
                    $"Repository \"{repositoryName}\" doesn't exist on db context {dbContext.Engine.Identifier}",
                    nameof(repositoryName));
            if (repository.IsReadOnly)
                throw new UnauthorizedAccessException(
                    $"Can't repair the missing origin references of collection \"{repositoryName}\": the repository is read-only");

            var repairOp = new ReferencesRepairOperation(
                dbContext.Engine,
                repositoryName,
                repairModesByElementPath,
                dryRun);

            var started = await DbOperationLifecycle.TryStartAsync(
                dbContext,
                repairOp,
                lockLeaseDuration,
                () => taskRunner.RunRepairReferencesTask(dbContext.GetType(), repairOp.Id),
                logger).ConfigureAwait(false);

            return started ? repairOp : null;
        }

    }
}
