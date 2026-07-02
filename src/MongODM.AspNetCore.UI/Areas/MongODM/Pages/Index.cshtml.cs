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

using Etherna.MongoDB.Bson;
using Etherna.MongODM.Core;
using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.Domain.Models.DbMigrationOpAgg;
using Etherna.MongODM.Core.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Etherna.MongODM.AspNetCore.UI.Areas.MongODM.Pages
{
    [IgnoreAntiforgeryToken]
    public class IndexModel : PageModel
    {
        // Consts.
        private const int HistoryLength = 5;

        // Fields.
        private readonly MongODMOptions options;
        private readonly IServiceProvider serviceProvider;

        // Constructor.
        public IndexModel(
            IOptions<MongODMOptions> options,
            IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(options);

            this.options = options.Value;
            this.serviceProvider = serviceProvider;
        }

        // Properties.
        public IEnumerable<IDbContext> DbContexts { get; private set; } = null!;

        // Methods.
        public void OnGet()
        {
            InitializePage();
        }

        public async Task<IActionResult> OnGetStatusAsync()
        {
            InitializePage();

            var statuses = new List<object>();
            foreach (var dbContext in DbContexts)
            {
                var runningOperation = await dbContext.DbMigrationManager.IsMigrationRunningAsync().ConfigureAwait(false);
                var lastOperations = await dbContext.DbMigrationManager.GetLastMigrationsAsync(0, HistoryLength).ConfigureAwait(false);

                statuses.Add(new
                {
                    identifier = dbContext.Identifier,
                    isLocked = runningOperation is not null || dbContext.IsExclusiveWriteEnabled,
                    runningOperation = runningOperation is null ? null : ProjectOperation(runningOperation),
                    lastOperations = lastOperations
                        .Where(op => op.Id != runningOperation?.Id)
                        .Select(ProjectOperation)
                });
            }

            return new JsonResult(statuses);
        }

        public async Task<IActionResult> OnPostStartMigrationAsync(string identifier)
        {
            InitializePage();

            var dbContext = DbContexts.FirstOrDefault(dbc => dbc.Identifier == identifier);
            if (dbContext is null)
                return NotFound();

            var migrationOperation = await dbContext.TryStartMigrationAsync().ConfigureAwait(false);

            return new JsonResult(new
            {
                started = migrationOperation is not null,
                operationId = migrationOperation?.Id
            });
        }

        // Helpers.
        private void InitializePage()
        {
            // Get dbcontext instances.
            var dbContextTypes = options.DbContextTypes;
            DbContexts = dbContextTypes.Select(type => (IDbContext)serviceProvider.GetRequiredService(type));
        }

        private static object ProjectOperation(DbMigrationOperation operation) => new
        {
            id = operation.Id,
            status = operation.CurrentStatus.ToString(),
            //CreationDateTime is not persisted, derive the creation instant from the ObjectId
            creationDateTime = ObjectId.TryParse(operation.Id, out var objectId) ? objectId.CreationTime : (DateTime?)null,
            completedDateTime = operation.CompletedDateTime,
            logs = operation.Logs.Select(log => new
            {
                state = log.State.ToString(),
                creationDateTime = log.CreationDateTime,
                description = log switch
                {
                    BuildNewIndexesMigrationLog buildLog => $"Build new indexes on \"{buildLog.Repository}\"",
                    DeleteOldIndexesMigrationLog deleteLog => $"Delete old indexes on \"{deleteLog.Repository}\"",
                    DocumentMigrationLog docLog => $"Migrate documents on \"{docLog.CollectionName}\" ({docLog.TotMigratedDocs} docs)",
                    _ => log.GetType().Name
                }
            })
        };
    }
}
