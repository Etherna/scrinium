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
using Etherna.MongoDB.Bson.IO;
using Etherna.MongoDB.Bson.Serialization;
using Etherna.MongoDB.Driver;
using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.Domain.Models.DbMigrationOpAgg;
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.Core.Utility;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core.Migration
{
    public abstract class DocumentMigration
    {
        // Consts.
        /// <summary>
        /// Maximum number of failing documents detailed by a dry run result.
        /// </summary>
        public const int MaxTrackedDocumentErrors = 100;

        // Properties.
        public abstract IRepository SourceRepository { get; }

        // Methods.
        /// <summary>
        /// Perform migration with optional updating callback
        /// </summary>
        /// <param name="callbackEveryTotDocuments">Interval of processed documents between callback invokations. 0 if ignore callback</param>
        /// <param name="callbackAsync">The async callback function. Parameter is number of processed documents</param>
        /// <param name="dryRun">If true, simulate the migration without persisting anything:
        /// each document deserializes and processes with simulated collection writes, and each
        /// failing document reports into the result errors instead of aborting the migration</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The migration result</returns>
        public abstract Task<MigrationResult> MigrateAsync(
            int callbackEveryTotDocuments = 0,
            Func<long, Task>? callbackAsync = null,
            bool dryRun = false,
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
        public override IRepository SourceRepository => _sourceRepository;

        // Methods.
        public override Task<MigrationResult> MigrateAsync(
            int callbackEveryTotDocuments = 0,
            Func<long, Task>? callbackAsync = null,
            bool dryRun = false,
            CancellationToken cancellationToken = default) =>
            _sourceRepository.AccessToCollectionAsync(async sourceCollection =>
            {
                List<DocumentMigrationError> documentErrors = [];
                var totDocumentErrors = 0L;
                var totMigratedDocuments = 0L;
                try
                {
                    if (callbackEveryTotDocuments < 0)
                        throw new ArgumentOutOfRangeException(nameof(callbackEveryTotDocuments), "Value can't be negative");

                    if (dryRun)
                    {
                        // Simulate documents migration.
                        /* Scan raw documents and deserialize each one apart: a document failing
                         * deserialization reports its error without aborting the scan, which a
                         * typed cursor can't grant. The processor executes with an ambient dry
                         * run scope, simulating every write it performs. */
                        await sourceCollection.Find(FilterDefinition<TModel>.Empty, new FindOptions { NoCursorTimeout = true })
                            .As<BsonDocument>()
                            .ForEachAsync(async document =>
                            {
                                try
                                {
                                    // Deserialize the model like the typed find of a real migration would.
                                    TModel model;
                                    using (var documentReader = new BsonDocumentReader(document))
                                        model = sourceCollection.DocumentSerializer.Deserialize(
                                            BsonDeserializationContext.CreateRoot(documentReader));

                                    // Process the model with simulated writes.
                                    using (new DryRunHandler(_sourceRepository.DbContext.Engine.ExecutionContext))
                                        await sourceModelProcessorActionAsync(model).ConfigureAwait(false);
                                }
                                catch (Exception e) when (e is not OperationCanceledException)
                                {
                                    totDocumentErrors++;
                                    if (documentErrors.Count < MaxTrackedDocumentErrors)
                                        documentErrors.Add(new DocumentMigrationError(
                                            document.TryGetValue("_id", out var documentId) ? documentId.ToString()! : "?",
                                            $"{e.GetType().Name}: {e.Message}"));
                                }

                                // Increment counter.
                                totMigratedDocuments++;

                                // Execute callback.
                                if (callbackEveryTotDocuments > 0 &&
                                    totMigratedDocuments % callbackEveryTotDocuments == 0 &&
                                    callbackAsync != null)
                                    await callbackAsync(totMigratedDocuments).ConfigureAwait(false);

                            }, cancellationToken).ConfigureAwait(false);

                        if (totDocumentErrors > 0)
                            return MigrationResult.Failed(
                                totMigratedDocuments,
                                documentErrors: documentErrors,
                                totDocumentErrors: totDocumentErrors);
                    }
                    else
                    {
                        // Migrate documents.
                        await sourceCollection.Find(FilterDefinition<TModel>.Empty, new FindOptions { NoCursorTimeout = true })
                            .ForEachAsync(async model =>
                            {
                                await sourceModelProcessorActionAsync(model).ConfigureAwait(false);

                                // Increment counter.
                                totMigratedDocuments++;

                                // Execute callback.
                                if (callbackEveryTotDocuments > 0 &&
                                    totMigratedDocuments % callbackEveryTotDocuments == 0 &&
                                    callbackAsync != null)
                                    await callbackAsync(totMigratedDocuments).ConfigureAwait(false);

                            }, cancellationToken).ConfigureAwait(false);
                    }

                    return MigrationResult.Succeeded(totMigratedDocuments);
                }
                catch (Exception e)
                {
                    return MigrationResult.Failed(totMigratedDocuments, e, documentErrors, totDocumentErrors);
                }
            });
    }
}
