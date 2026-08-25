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

            var dbMigrationOp = (DbMigrationOperation)await dbContext.DbOperations.FindOneAsync(dbMigrationOpId).ConfigureAwait(false);

            /* A dry run doesn't persist anything: it runs without the in-process exclusive
             * access, keeping the collections available to the other flows. The db context
             * lock claimed with its operation still applies, denying the other migrations. */
            if (dbMigrationOp.IsDryRun)
                await dbContext.ExecuteMigrationAsync(dbMigrationOpId, taskId).ConfigureAwait(false);
            else
                await dbContext.Engine.RunWithExclusiveAccessAsync(() =>
                    dbContext.ExecuteMigrationAsync(dbMigrationOpId, taskId)).ConfigureAwait(false);
        }
    }
}
