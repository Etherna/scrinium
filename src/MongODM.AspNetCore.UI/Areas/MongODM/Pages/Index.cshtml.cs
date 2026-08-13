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
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.Core.Serialization.Mapping;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Etherna.MongODM.AspNetCore.UI.Areas.MongODM.Pages
{
    public class IndexModel : PageModel
    {
        // Consts.
        /// <summary>
        /// Longest db context lock lease duration accepted by a migration start, in minutes:
        /// a whole day. A longer lease would keep the db context locked for days after an
        /// instance dies, with no way to start a migration or a seeding of it meanwhile.
        /// </summary>
        public const int MaxLockLeaseDurationMinutes = 24 * 60;

        /* The dashboard is self contained: its style sheet and script are served with the page,
         * and no other asset, inline code or external source takes part in it. */
        private const string ContentSecurityPolicy =
            "default-src 'none'; " +
            "base-uri 'none'; " +
            "connect-src 'self'; " +
            "form-action 'none'; " +
            "frame-ancestors 'none'; " +
            "img-src 'self'; " +
            "script-src 'self'; " +
            "style-src 'self'";
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
        /// <summary>
        /// Get the model map schemas that can stamp their id on the documents of a repository
        /// collection: those of the concrete model types assignable to the repository model
        /// type, since a document carries the active schema id of its own concrete type.
        /// Fallback schemas stay out: their reserved id is never written on documents.
        /// </summary>
        /// <param name="repository">The collection repository</param>
        /// <returns>Model type name, schema id and active flag of each schema</returns>
        public static IEnumerable<(string ModelTypeName, string SchemaId, bool IsActiveSchema)> GetDocumentSchemas(
            IRepository repository)
        {
            ArgumentNullException.ThrowIfNull(repository);

            return repository.DbContext.Engine.MapRegistry.MapsByModelType.Values
                .OfType<IModelMap>()
                .Where(map => !map.ModelType.IsAbstract &&
                    repository.ModelType.IsAssignableFrom(map.ModelType))
                .OrderBy(map => map.ModelType.Name)
                .SelectMany(map => map.SecondarySchemas
                    .Select(schema => (ModelTypeName: map.ModelType.Name, SchemaId: schema.Id, IsActiveSchema: false))
                    .Prepend((ModelTypeName: map.ModelType.Name, SchemaId: map.ActiveSchema.Id, IsActiveSchema: true)));
        }

        public void OnGet()
        {
            InitializePage();
        }

        /// <summary>
        /// Size every collection of a db context reading its metadata: a constant cost per
        /// collection, telling how much a schema ids count would have to scan.
        /// </summary>
        public async Task<IActionResult> OnGetCollectionSizesAsync(string identifier)
        {
            InitializePage();

            var dbContext = DbContexts.FirstOrDefault(dbc => dbc.Engine.Identifier == identifier);
            if (dbContext is null)
                return NotFound();

            var collections = new List<object>();
            foreach (var repository in dbContext.RepositoryRegistry.Repositories.OrderBy(r => r.Name))
            {
                long? estimatedDocumentsCount = null;
                try
                {
                    estimatedDocumentsCount = await repository.EstimatedDocumentCountAsync().ConfigureAwait(false);
                }
                catch (UnauthorizedAccessException)
                {
                    /* An exclusive access (a running migration) denies reads on the collection:
                     * report it as unavailable, instead of failing the whole request. */
                }

                collections.Add(new
                {
                    repository = repository.Name,
                    isUnavailable = estimatedDocumentsCount is null,
                    estimatedDocumentsCount
                });
            }

            return new JsonResult(collections);
        }

        /// <summary>
        /// Count the documents of a single collection by schema id. This scans the whole
        /// collection, so it runs only on explicit request, one collection at a time.
        /// </summary>
        public async Task<IActionResult> OnGetSchemaCountsAsync(string identifier, string repositoryName)
        {
            InitializePage();

            var dbContext = DbContexts.FirstOrDefault(dbc => dbc.Engine.Identifier == identifier);
            var repository = dbContext?.RepositoryRegistry.Repositories
                .FirstOrDefault(repo => repo.Name == repositoryName);
            if (repository is null)
                return NotFound();

            IReadOnlyDictionary<string, long>? documentsBySchemaId = null;
            var documentsWithoutSchemaId = 0L;
            try
            {
                (documentsBySchemaId, documentsWithoutSchemaId) =
                    await repository.CountDocumentsBySchemaIdAsync().ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                //an exclusive access (a running migration) denies reads on the collection
            }

            return new JsonResult(new
            {
                repository = repository.Name,
                isUnavailable = documentsBySchemaId is null,
                //a list of pairs, instead of a map: schema ids are values, never json keys
                schemaCounts = documentsBySchemaId?.Select(pair => new
                {
                    schemaId = pair.Key,
                    documentsCount = pair.Value
                }),
                documentsWithoutSchemaId
            });
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

                /* An operation stays open also when the instance executing it dies: a migration
                 * is in progress only while the db context lock lease of its owner is alive.
                 * Reporting an orphaned operation as running would disable the start controls
                 * forever, while a start is the only thing closing the orphaned operations. */
                var openOperation = await dbContext.IsMigrationRunningAsync().ConfigureAwait(false);
                var runningOperation =
                    openOperation is not null &&
                    await dbContext.Engine.DbContextLock.IsLockedAsync().ConfigureAwait(false)
                        ? openOperation
                        : null;
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

        public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            /* Every dashboard response reports the live state of a database, or starts a
             * migration on it: none of them may be stored, or have its content type sniffed. */
            var headers = Response.Headers;
            headers.CacheControl = "no-store";
            headers.XContentTypeOptions = "nosniff";

            /* The page carries the controls changing the state of the database: it denies any
             * framing of them, and any content source other than its own assets. */
            if (context.HandlerMethod?.MethodInfo.Name == nameof(OnGet))
            {
                headers.ContentSecurityPolicy = ContentSecurityPolicy;
                headers.XFrameOptions = "DENY";
            }
        }

        public async Task<IActionResult> OnPostStartMigrationAsync(
            string identifier,
            bool dryRun = false,
            bool stopAtFirstError = false,
            int? lockLeaseDurationMinutes = null)
        {
            InitializePage();

            var dbContext = DbContexts.FirstOrDefault(dbc => dbc.Engine.Identifier == identifier);
            if (dbContext is null)
                return NotFound();

            /* The lease duration arrives from the browser: the page controls bound it, but the
             * request doesn't have to come from them. A missing or non positive lease can't
             * claim anything, and an unbounded one would keep the db context locked for as long
             * as it says if this instance dies. */
            if (lockLeaseDurationMinutes is not > 0)
                return BadRequest(new
                {
                    started = false,
                    error = "The lock lease duration must be a positive number of minutes."
                });
            if (lockLeaseDurationMinutes > MaxLockLeaseDurationMinutes)
                return BadRequest(new
                {
                    started = false,
                    error = $"The lock lease duration can't exceed {MaxLockLeaseDurationMinutes} minutes."
                });

            var migrationOperation = await dbContext.TryStartMigrationAsync(
                dryRun,
                stopAtFirstError,
                TimeSpan.FromMinutes(lockLeaseDurationMinutes.Value)).ConfigureAwait(false);

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
            stopAtFirstError = operation.IsStopAtFirstErrorEnabled,
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
