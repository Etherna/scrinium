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
using Etherna.MongoDB.Driver.Linq;
using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.Exceptions;
using Etherna.MongODM.Core.Extensions;
using Etherna.MongODM.Core.Migration;
using Etherna.MongODM.Core.Options;
using Etherna.MongODM.Core.ProxyModels;
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.Core.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core
{
    public abstract class DbContext(ILogger? logger = null)
        : IDbContext, IDbContextBuilder
    {
        // Fields.
        private readonly HashSet<IEntityModel> changedModels = [];
        private IEnumerable<IDbContext> childDbContexts = null!;
        private readonly Dictionary<(IRepository Repository, object ModelId), IEntityModel> loadedModels = [];
        private IDbContextEngine engine = null!;
        private readonly ILogger logger = logger ?? NullLogger.Instance;
        private IRepositoryRegistry? scopedRepositoryRegistry;

        // Initializer.
        public void AttachToEngine(
            IDbContextEngine engine,
            IEnumerable<IDbContext> childDbContexts,
            IRepositoryRegistry repositoryRegistry)
        {
            ArgumentNullException.ThrowIfNull(engine);
            ArgumentNullException.ThrowIfNull(repositoryRegistry);
            if (this.engine is not null)
                throw new InvalidOperationException(
                    "DbContext already initialized. Register db contexts with a factory to create an instance for each scope");

            this.childDbContexts = childDbContexts;
            this.engine = engine;

            // Initialize instance repositories with their own registry.
            DbOperations = new Repository<OperationBase, string>(engine.Options.DbOperationsCollectionName);

            repositoryRegistry.Initialize(this, logger);
            foreach (var repository in repositoryRegistry.Repositories)
                if (!repository.IsInitialized)
                    repository.Initialize(this, logger);
            scopedRepositoryRegistry = repositoryRegistry;
        }

        public IDbContextEngine BuildEngine(
            IDbDependencies dependencies,
            IMongoClient mongoClient,
            IDbContextOptions options)
        {
            var newEngine = new DbContextEngine(logger);
            newEngine.Initialize(
                dependencies,
                mongoClient,
                options,
                GetType(),
                ModelMapsCollectors);
            return newEngine;
        }

        // Public properties.
        public IReadOnlyCollection<IEntityModel> ChangedModelsList
        {
            get
            {
                lock (changedModels)
                    return changedModels
                        .Where(model => model is IAuditable { IsChanged: true })
                        .ToList();
            }
        }
        public IRepository<OperationBase, string> DbOperations { get; private set; } = null!;
        public virtual IEnumerable<DocumentMigration> DocumentMigrationList { get; } = [];
        public IDbContextEngine Engine => engine;
        public bool IsSeeded
        {
            get
            {
                // Try to read cached.
                var cached = engine.IsSeededCache;
                if (cached.HasValue)
                    return cached.Value;

                // Get seeding state from db.
                var task = DbOperations.QueryElementsAsync(elements =>
                        elements.OfType<SeedOperation>()
                                .AnyAsync(sop => sop.DbContextName == engine.Identifier));
                task.Wait();

                engine.IsSeededCache = task.Result;
                return task.Result;
            }
        }
        public IRepositoryRegistry RepositoryRegistry => scopedRepositoryRegistry!;

        // Protected properties.
        protected abstract IEnumerable<IModelMapsCollector> ModelMapsCollectors { get; }

        // Methods.
        public Task ExecuteMigrationAsync(string dbMigrationOpId, string? taskId = null, bool throwOnErrors = false) =>
            engine.DbMigrationManager.ExecuteDbContextMigrationAsync(this, dbMigrationOpId, taskId, throwOnErrors);

        public Task<List<DbMigrationOperation>> GetLastMigrationsAsync(int page, int take) =>
            engine.DbMigrationManager.GetLastMigrationsAsync(this, page, take);

        public Task<DbMigrationOperation> GetMigrationAsync(string migrateOperationId) =>
            engine.DbMigrationManager.GetMigrationAsync(this, migrateOperationId);

        public Task<DbMigrationOperation?> IsMigrationRunningAsync() =>
            engine.DbMigrationManager.IsMigrationRunningAsync(this);

        public void RegisterChangedModel(IEntityModel model)
        {
            ArgumentNullException.ThrowIfNull(model);

            lock (changedModels)
                changedModels.Add(model);
        }

        public void RegisterLoadedModel(object modelId, IEntityModel model)
        {
            ArgumentNullException.ThrowIfNull(modelId);
            ArgumentNullException.ThrowIfNull(model);

            var repository = TryGetRepositoryForModelType(engine.ProxyGenerator.PurgeProxyType(model.GetType()));
            if (repository is null) //identity is meaningless without a repository
                return;

            lock (loadedModels)
                loadedModels[(repository, modelId)] = model;
        }

        public virtual async Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            /*
             * Currently at MongoDB 4.0 sessions are only available for Replica Sets.
             * This exclude the development environment from use them, so in order to have a more
             * similar set up in development and production it's better to disable them, for now.
             */

            //using (var session = await StartSessionAsync())
            //{
            //    session.StartTransaction();

            //    try
            //    {
            //        // Commit updated models replacement.
            //        foreach (var model in DBCache.LoadedModels.Values
            //            .Where(model => (model as IAuditable).IsChanged)
            //            .ToList())
            //        {
            //            var repository = ModelCollectionRepositoryMap[model.GetType().BaseType];
            //            await repository.ReplaceAsync(model, session);
            //        }
            //    }
            //    catch
            //    {
            //        await session.AbortTransactionAsync();
            //        throw;
            //    }

            //    await session.CommitTransactionAsync();
            //}

            // Commit updated models replacement.
            foreach (var model in ChangedModelsList)
            {
                var modelType = engine.ProxyGenerator.PurgeProxyType(model.GetType());

                var repository = RepositoryRegistry.TryGetRepositoryByHandledModelType(modelType);
                if (repository != null)
                {
                    await repository.ReplaceAsync(model, cancellationToken: cancellationToken).ConfigureAwait(false);

                    logger.DbContextSavedChangedModelToRepository(engine.Options.DbName, repository.ModelIdToString(model), repository.Name);
                }
            }

            // Save changes on child dbcontexts.
            foreach (var child in childDbContexts)
            {
                await child.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            logger.DbContextSavedChanges(engine.Options.DbName);
        }

        public async Task<bool> SeedIfNeededAsync()
        {
            // Check if already seeded.
            if (IsSeeded)
                return false;

            return await engine.RunWithExclusiveAccessAsync(async () =>
            {
                // Check again if seeded.
                if (IsSeeded)
                    return false;

                // Apply db migration, blocking seed in case of errors.
                // This creates indexes by default on each new database.
                var dbMigrationOp = new DbMigrationOperation(engine);
                await DbOperations.CreateAsync(dbMigrationOp).ConfigureAwait(false);
                await ExecuteMigrationAsync(dbMigrationOp.Id, throwOnErrors: true).ConfigureAwait(false);

                // Seed.
                try { await SeedAsync().ConfigureAwait(false); }
                catch (Exception e) { throw new MongodmDbSeedingException($"Error seeding {GetType().Name} dbContext", e); }

                // Report operation.
                var seedOperation = new SeedOperation(engine);
                await DbOperations.CreateAsync(seedOperation).ConfigureAwait(false);

                // Cache as seeded.
                engine.IsSeededCache = true;

                logger.DbContextSeeded(engine.Options.DbName);

                return true;
            }).ConfigureAwait(false);
        }

        public IEntityModel? TryGetLoadedModel(Type modelType, object modelId)
        {
            ArgumentNullException.ThrowIfNull(modelType);
            ArgumentNullException.ThrowIfNull(modelId);

            var repository = TryGetRepositoryForModelType(modelType);
            if (repository is null)
                return null;

            lock (loadedModels)
                return loadedModels.TryGetValue((repository, modelId), out var model) ? model : null;
        }

        public Task<DbMigrationOperation?> TryStartMigrationAsync() =>
            engine.DbMigrationManager.TryStartDbContextMigrationAsync(this);

        public void UnregisterChangedModel(IEntityModel model)
        {
            ArgumentNullException.ThrowIfNull(model);

            lock (changedModels)
                changedModels.Remove(model);
        }

        public void UnregisterLoadedModel(object modelId, IEntityModel model)
        {
            ArgumentNullException.ThrowIfNull(modelId);
            ArgumentNullException.ThrowIfNull(model);

            var repository = TryGetRepositoryForModelType(engine.ProxyGenerator.PurgeProxyType(model.GetType()));
            if (repository is null)
                return;

            lock (loadedModels)
            {
                //remove only if this same instance is the registered one
                if (loadedModels.TryGetValue((repository, modelId), out var loadedModel) &&
                    ReferenceEquals(loadedModel, model))
                    loadedModels.Remove((repository, modelId));
            }
        }

        // Protected methods.
        protected virtual Task SeedAsync() =>
            Task.CompletedTask;

        // Helpers.
        private IRepository? TryGetRepositoryForModelType(Type modelType) =>
            scopedRepositoryRegistry?.TryGetRepositoryByHandledModelType(modelType);
    }
}
