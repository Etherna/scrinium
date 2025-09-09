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
using Etherna.MongODM.Core.Domain.Models.DbMigrationOpAgg;
using System;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core.Tasks
{
    public class MigrateDbContextTask(IServiceProvider serviceProvider)
        : IMigrateDbContextTask
    {
        // Methods.
        public async Task RunAsync<TDbContext>(string dbMigrationOpId, string taskId)
            where TDbContext : class, IDbContext
        {
            var dbContext = (TDbContext)serviceProvider.GetService(typeof(TDbContext))!;
            
            // Run with exclusive access.
            await dbContext.RunWithExclusiveAccessAsync(async () =>
            {
                var dbMigrationOp = (DbMigrationOperation)await dbContext.DbOperations.FindOneAsync(dbMigrationOpId).ConfigureAwait(false);
                var completedWithErrors = false;

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
                    catch (Exception)
                    {
                        completedWithErrors = true;

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
                        completedWithErrors = true;

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
                    catch (Exception)
                    {
                        completedWithErrors = true;

                        dbMigrationOp.AddLog(new BuildNewIndexesMigrationLog(
                            repository.Name,
                            MigrationLogBase.ExecutionState.Failed));
                    }

                    await dbContext.SaveChangesAsync().ConfigureAwait(false);
                }

                // Complete task.
                if (!completedWithErrors)
                    dbMigrationOp.TaskCompleted();
                else
                    dbMigrationOp.TaskFailed();

                await dbContext.SaveChangesAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
    }
}
