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

using Etherna.MongoDB.Driver;
using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.Repositories;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core.Migration
{
    public abstract class DocumentMigration
    {
        // Properties.
        public abstract IRepository SourceRepository { get; }

        // Methods.
        /// <summary>
        /// Perform migration with optional updating callback
        /// </summary>
        /// <param name="callbackEveryTotDocuments">Interval of processed documents between callback invokations. 0 if ignore callback</param>
        /// <param name="callbackAsync">The async callback function. Parameter is number of processed documents</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The migration result</returns>
        public abstract Task<MigrationResult> MigrateAsync(int callbackEveryTotDocuments = 0, Func<long, Task>? callbackAsync = null, CancellationToken cancellationToken = default);
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
            CancellationToken cancellationToken = default) =>
            _sourceRepository.AccessToCollectionAsync(async sourceCollection =>
            {
                var totMigratedDocuments = 0L;
                try
                {
                    if (callbackEveryTotDocuments < 0)
                        throw new ArgumentOutOfRangeException(nameof(callbackEveryTotDocuments), "Value can't be negative");

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

                    return MigrationResult.Succeeded(totMigratedDocuments);
                }
                catch (Exception e)
                {
                    return MigrationResult.Failed(totMigratedDocuments, e);
                }
            });
    }
}
