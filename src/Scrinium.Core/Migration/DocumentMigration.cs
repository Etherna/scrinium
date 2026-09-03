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
using Etherna.MongoDB.Bson.IO;
using Etherna.MongoDB.Bson.Serialization;
using Etherna.MongoDB.Driver;
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Domain.Models.DbMigrationOpAgg;
using Etherna.Scrinium.Core.Repositories;
using Etherna.Scrinium.Core.Utility;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.Scrinium.Core.Migration
{
    public abstract class DocumentMigration
    {
        // Consts.
        /// <summary>
        /// Documents processed by a scan between two evictions of its transient models, when
        /// the migration doesn't ask for another interval.
        /// </summary>
        public const int DefaultEvictEveryTotDocuments = 100;

        /// <summary>
        /// Maximum number of failing documents detailed by a migration result.
        /// </summary>
        public const int MaxTrackedDocumentErrors = 100;

        // Properties.
        public abstract IRepository SourceRepository { get; }

        // Methods.
        /// <summary>
        /// Perform migration with optional updating callback
        /// </summary>
        /// <param name="callbackEveryTotDocuments">Interval of processed documents between callback invokations. 0 if ignore callback</param>
        /// <param name="callbackAsync">The async callback function. Parameter is number of migrated documents</param>
        /// <param name="dryRun">If true, simulate the migration without persisting anything:
        /// each document processes with simulated collection writes</param>
        /// <param name="stopAtFirstError">If true, abort the migration at the first failing
        /// document, instead of skipping it and processing every other document</param>
        /// <param name="evictEveryTotDocuments">Interval of processed documents between the
        /// evictions of what the scan loaded and tracked on the db context. It bounds the scan
        /// memory to the documents of an interval, with their referenced summaries, and it is
        /// independent from the callback interval: the two tune different costs</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The migration result</returns>
        public abstract Task<MigrationResult> MigrateAsync(
            int callbackEveryTotDocuments = 0,
            Func<long, Task>? callbackAsync = null,
            bool dryRun = false,
            bool stopAtFirstError = false,
            int evictEveryTotDocuments = DefaultEvictEveryTotDocuments,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Migrate documents of a collection
    /// </summary>
    /// <typeparam name="TModel">The model type</typeparam>
    /// <typeparam name="TKey">The model's key type</typeparam>
    public class DocumentMigration<TModel, TKey>(
        IRepository<TModel, TKey> sourceRepository,
        Func<TModel, Task> sourceModelProcessorActionAsync)
        : DocumentMigration
        where TModel : class, IEntityModel<TKey>
    {
        // Fields.
        private readonly IRepository<TModel, TKey> _sourceRepository =
            sourceRepository ?? throw new ArgumentNullException(nameof(sourceRepository));

        // Constructors.
        public DocumentMigration(IRepository<TModel, TKey> repository)
            : this(repository, repository, m => m)
        { }

        public DocumentMigration(
            IRepository<TModel, TKey> sourceRepository,
            IRepository destinationRepository,
            Func<TModel, object> modelConverter)
            : this(sourceRepository, _ => destinationRepository, modelConverter)
        { }

        public DocumentMigration(
            IRepository<TModel, TKey> sourceRepository,
            Func<TModel, IRepository?> destinationRepositorySelector,
            Func<TModel, object> modelConverter)
            : this(
                sourceRepository,
                async m =>
                {
                    var destinationRepository = destinationRepositorySelector(m);

                    // Verify if needs to skip this model.
                    if (destinationRepository is null)
                        return;

                    // Replace if it's the same collection, insert one otherwise.
                    if (sourceRepository == destinationRepository)
                        await destinationRepository.ReplaceAsync(m, updateDependentDocuments: false).ConfigureAwait(false);
                    else
                        await destinationRepository.CreateAsync(modelConverter(m)).ConfigureAwait(false);
                })
        { }

        // Properties.
        /// <summary>
        /// The documents to migrate, every one of them by default. It selects them on their
        /// stored content, since a migration also addresses documents the current maps don't
        /// shape anymore.
        /// </summary>
        public FilterDefinition<TModel> DocumentsFilter { get; init; } = FilterDefinition<TModel>.Empty;

        public override IRepository SourceRepository => _sourceRepository;

        // Methods.
        public override Task<MigrationResult> MigrateAsync(
            int callbackEveryTotDocuments = 0,
            Func<long, Task>? callbackAsync = null,
            bool dryRun = false,
            bool stopAtFirstError = false,
            int evictEveryTotDocuments = DefaultEvictEveryTotDocuments,
            CancellationToken cancellationToken = default) =>
            _sourceRepository.AccessToCollectionAsync(async sourceCollection =>
            {
                List<DocumentMigrationError> documentErrors = [];
                var totDocumentErrors = 0L;
                var totMigratedDocuments = 0L;
                var totProcessedDocuments = 0L;
                try
                {
                    if (callbackEveryTotDocuments < 0)
                        throw new ArgumentOutOfRangeException(nameof(callbackEveryTotDocuments), "Value can't be negative");
                    ArgumentOutOfRangeException.ThrowIfLessThan(evictEveryTotDocuments, 1);

                    /* Documents processed since the last eviction run inside a transient models
                     * scope of the db context: everything their flows load or track evicts at
                     * the scope end, so the scan memory stays bounded to an eviction interval
                     * whatever the collection size. The migration operation bookkeeping, tracked
                     * before the scan, stays tracked and updates from the callback. */
                    IDisposable? transientModelsScope = null;
                    try
                    {
                        // Migrate documents.
                        /* Scan raw documents and deserialize each one apart: a document failing
                         * deserialization reports its error without aborting the scan, which a
                         * typed cursor can't grant. The cursor lives as long as the session the
                         * driver keeps alive for it: the server side idle timeout of a cursor
                         * only reaps the cursors without a session, so a long scan doesn't need
                         * to disable it. */
                        await sourceCollection.Find(DocumentsFilter)
                            .As<BsonDocument>()
                            .ForEachAsync(async document =>
                            {
                                // Increment counter.
                                totProcessedDocuments++;

                                transientModelsScope ??= _sourceRepository.DbContext.StartTransientModelsScope();

                                try
                                {
                                    // Deserialize the model.
                                    TModel model;
                                    using (var documentReader = new BsonDocumentReader(document))
                                        model = sourceCollection.DocumentSerializer.Deserialize(
                                            BsonDeserializationContext.CreateRoot(documentReader));

                                    // Process the model. A dry run simulates every write it performs.
                                    using (dryRun ? new DryRunHandler(_sourceRepository.DbContext.Engine.ExecutionContext) : null)
                                        await sourceModelProcessorActionAsync(model).ConfigureAwait(false);

                                    totMigratedDocuments++;
                                }
                                catch (Exception e) when (e is not OperationCanceledException)
                                {
                                    // Report the failing document, leaving it on its current content.
                                    totDocumentErrors++;
                                    if (documentErrors.Count < MaxTrackedDocumentErrors)
                                        documentErrors.Add(new DocumentMigrationError(
                                            document.TryGetValue("_id", out var documentId) ? documentId.ToString()! : "?",
                                            $"{e.GetType().Name}: {e.Message}"));

                                    if (stopAtFirstError)
                                        throw;
                                }

                                // Evict what the interval documents loaded and tracked.
                                if (totProcessedDocuments % evictEveryTotDocuments == 0)
                                {
                                    transientModelsScope.Dispose();
                                    transientModelsScope = null;
                                }

                                // Execute callback.
                                if (callbackEveryTotDocuments > 0 &&
                                    totProcessedDocuments % callbackEveryTotDocuments == 0 &&
                                    callbackAsync != null)
                                    await callbackAsync(totMigratedDocuments).ConfigureAwait(false);

                            }, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        //evict what the documents of the last, incomplete interval left
                        transientModelsScope?.Dispose();
                    }

                    return totDocumentErrors == 0
                        ? MigrationResult.Succeeded(totMigratedDocuments)
                        : MigrationResult.Failed(
                            totMigratedDocuments,
                            documentErrors: documentErrors,
                            totDocumentErrors: totDocumentErrors);
                }
                catch (Exception e)
                {
                    return MigrationResult.Failed(totMigratedDocuments, e, documentErrors, totDocumentErrors);
                }
            });
    }
}
