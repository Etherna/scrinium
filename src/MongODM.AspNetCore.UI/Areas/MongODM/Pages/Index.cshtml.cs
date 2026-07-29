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
                // Read-only db contexts deny migrations: no migration state to report.
                if (dbContext.Engine.Options.IsReadOnly)
                    continue;

                var runningOperation = await dbContext.IsMigrationRunningAsync().ConfigureAwait(false);
                var lastOperations = await dbContext.GetLastMigrationsAsync(0, HistoryLength).ConfigureAwait(false);

                statuses.Add(new
                {
                    identifier = dbContext.Engine.Identifier,
                    isLocked = runningOperation is not null || dbContext.Engine.IsExclusiveWriteEnabled,
                    runningOperation = runningOperation is null ? null : ProjectOperation(runningOperation),
                    lastOperations = lastOperations
                        .Where(op => op.Id != runningOperation?.Id)
                        .Select(ProjectOperation)
                });
            }

            return new JsonResult(statuses);
        }

        public async Task<IActionResult> OnPostStartMigrationAsync(string identifier, bool dryRun = false)
        {
            InitializePage();

            var dbContext = DbContexts.FirstOrDefault(dbc => dbc.Engine.Identifier == identifier);
            if (dbContext is null)
                return NotFound();

            var migrationOperation = await dbContext.TryStartMigrationAsync(dryRun).ConfigureAwait(false);

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
            isDryRun = operation.IsDryRun,
            status = operation.CurrentStatus.ToString(),
            //the ObjectId id embeds the creation instant
            creationDateTime = ObjectId.TryParse(operation.Id, out var objectId) ? new DateTimeOffset(objectId.CreationTime) : (DateTimeOffset?)null,
            completedDateTime = operation.CompletedDateTime,
            logs = operation.Logs.Select(log => new
            {
                state = log.State.ToString(),
                creationDateTime = log.CreationDateTime,
                description = log switch
                {
                    BuildNewIndexesMigrationLog buildLog => $"Build new indexes on \"{buildLog.Repository}\"",
                    DeleteOldIndexesMigrationLog deleteLog => $"Delete old indexes on \"{deleteLog.Repository}\"",
                    DocumentMigrationLog { TotErrorDocs: > 0 } docLog => $"Migrate documents on \"{docLog.CollectionName}\" ({docLog.TotMigratedDocs} docs, {docLog.TotErrorDocs} errors)",
                    DocumentMigrationLog docLog => $"Migrate documents on \"{docLog.CollectionName}\" ({docLog.TotMigratedDocs} docs)",
                    _ => log.GetType().Name
                },
                errors = (log as DocumentMigrationLog)?.Errors.Select(error => new
                {
                    documentId = error.DocumentId,
                    message = error.Message
                })
            })
        };
    }
}
