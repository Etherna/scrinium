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
using Etherna.MongODM.Core.Serialization.Mapping;
using Etherna.MongODM.Core.Utility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core
{
    public abstract class DbContext(ILogger? logger = null)
        : IDbContext, IDbContextBuilder
    {
        // Fields.
        /* Change tracking state keyed by reference identity: a model is tracked by its
         * serialized model document captured at load, and a proxy signals its mutations marking
         * itself a change candidate. Non proxy tracked models can't self signal, so they are all
         * diffed at save. EntityModelBase equates by id, so an id based comparer would collapse
         * distinct instances: identity is the required key here. */
        private readonly HashSet<object> changeCandidates = new(ReferenceEqualityComparer.Instance);
        private int changeTrackingSuppressions;
        private IEnumerable<IDbContext> childDbContexts = null!;
        private IDbContextEngine engine = null!;
        private readonly Dictionary<(IRepository Repository, object ModelId), IEntityModel> loadedModels = [];
        private readonly ILogger logger = logger ?? NullLogger.Instance;
        private readonly Dictionary<object, BsonDocument> modelBsonDocuments = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, IRepository> modelSourceRepositories = new(ReferenceEqualityComparer.Instance);
        private IRepositoryRegistry? scopedRepositoryRegistry;
        private readonly object trackingLock = new();
        private readonly HashSet<(Type ModelType, string? MemberName)> warnedImplicitLazyLoads = [];

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

            logger.DbContextAttachedToEngine(engine.Options.DbName);
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

            /* Resolve the implicit source repositories of reference serializers, and
             * validate the declared ones, accessing the repository properties of this
             * builder instance. */
            if (newEngine.MapRegistry is MapRegistry mapRegistry)
            {
                mapRegistry.ResolveImplicitSourceReferences(this);
                mapRegistry.ValidateDeclaredSourceReferences(this);
            }

            return newEngine;
        }

        // Public properties.
        public IReadOnlyCollection<IEntityModel> ChangedModelsList
        {
            get
            {
                lock (trackingLock)
                    return changeCandidates.Cast<IEntityModel>().ToList();
            }
        }
        public IRepository<OperationBase, string> DbOperations { get; private set; } = null!;
        public virtual IEnumerable<DocumentMigration> DocumentMigrationList { get; } = [];
        public IDbContextEngine Engine => engine;
        public bool IsChangeTrackingSuppressed
        {
            get
            {
                lock (trackingLock)
                    return changeTrackingSuppressions > 0;
            }
        }
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
        public Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(action);

            return ExecuteInTransactionAsync(async () =>
            {
                await action().ConfigureAwait(false);
                return 0;
            }, cancellationToken);
        }

        public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> func, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(func);

            using var session = await engine.StartSessionAsync(cancellationToken).ConfigureAwait(false);
            session.StartTransaction();
            logger.DbContextStartedTransaction(engine.Options.DbName);

            /* The session handler enlists in the transaction every operation invoked
             * without an explicit session on collections of this engine, for the whole
             * function execution. */
            using var sessionHandler = new DbSessionHandler(engine, session);

            TResult result;
            try
            {
                result = await func().ConfigureAwait(false);
            }
            catch
            {
                /* Abort with an uncancellable token: the function may have thrown for the
                 * cancellation itself, and the abort must run anyway. */
                await session.AbortTransactionAsync(CancellationToken.None).ConfigureAwait(false);
                logger.DbContextAbortedTransaction(engine.Options.DbName);
                throw;
            }

            await session.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
            logger.DbContextCommittedTransaction(engine.Options.DbName);

            return result;
        }

        public Task ExecuteMigrationAsync(string dbMigrationOpId, string? taskId = null, bool throwOnErrors = false) =>
            engine.DbMigrationManager.ExecuteDbContextMigrationAsync(this, dbMigrationOpId, taskId, throwOnErrors);

        public Task<List<DbMigrationOperation>> GetLastMigrationsAsync(int page, int take) =>
            engine.DbMigrationManager.GetLastMigrationsAsync(this, page, take);

        public Task<DbMigrationOperation> GetMigrationAsync(string migrateOperationId) =>
            engine.DbMigrationManager.GetMigrationAsync(this, migrateOperationId);

        public Task<DbMigrationOperation?> IsMigrationRunningAsync() =>
            engine.DbMigrationManager.IsMigrationRunningAsync(this);

        public bool IsMemberLoaded<TModel>(TModel model, Expression<Func<TModel, object?>> member)
            where TModel : class, IEntityModel
        {
            ArgumentNullException.ThrowIfNull(model);
            ArgumentNullException.ThrowIfNull(member);

            if (model is not IReferenceable { IsSummary: true } referenceable)
                return true;

            //the id member is definitionally present on any instance
            var memberName = ReflectionHelper.GetMemberInfoFromLambda(member).Name;
            if (TryGetIdMemberInfo(model.GetType())?.Name == memberName)
                return true;

            return referenceable.SettedMemberNames.Contains(memberName);
        }

        public bool IsOutdatedModel(object model)
        {
            ArgumentNullException.ThrowIfNull(model);

            return model is IProxyModel { OutdatedModelType: not null };
        }

        public Task LoadValuesAsync<TModel>(TModel model, params Expression<Func<TModel, object?>>[] members)
            where TModel : class, IEntityModel
        {
            ArgumentNullException.ThrowIfNull(model);
            return LoadValuesAsync([model], members);
        }

        public async Task LoadValuesAsync<TModel>(IEnumerable<TModel> models, params Expression<Func<TModel, object?>>[] members)
            where TModel : class, IEntityModel
        {
            ArgumentNullException.ThrowIfNull(models);
            ArgumentNullException.ThrowIfNull(members);

            var memberNames = members.Select(member => ReflectionHelper.GetMemberInfoFromLambda(member).Name).ToArray();

            /* Select the summary models still missing some requested member. The members are
             * only the no-op precondition: any load is always of the whole document. */
            List<(IEntityModel Model, IRepository Repository)> modelsToLoad = [];
            foreach (var model in models)
            {
                if (model is not IReferenceable { IsSummary: true } referenceable)
                    continue;

                //the id member is definitionally present, so it never requires a load
                var loadedMemberNames = referenceable.SettedMemberNames.ToHashSet(StringComparer.Ordinal);
                if (memberNames.All(name =>
                        loadedMemberNames.Contains(name) ||
                        TryGetIdMemberInfo(model.GetType())?.Name == name))
                    continue;

                var repository = referenceable.SourceRepository
                    ?? throw new InvalidOperationException(
                        $"Model of type {typeof(TModel).Name} is not bound to a db context scope, and can't load");
                modelsToLoad.Add((model, repository));
            }

            /* One query per source repository: the loaded documents deserialize on this
             * scope, merging in place into the summary instances through the identity map.
             * Custom repository implementations without the batch surface load per instance. */
            foreach (var repositoryGroup in modelsToLoad.GroupBy(pair => pair.Repository))
            {
                if (repositoryGroup.Key is IFullModelsLoader fullModelsLoader)
                {
                    await fullModelsLoader.LoadFullModelsAsync(
                        repositoryGroup.Select(pair => pair.Model)).ConfigureAwait(false);
                }
                else
                {
                    foreach (var (model, repository) in repositoryGroup)
                    {
                        var modelId = TryGetIdMemberInfo(model.GetType()) is { } idMemberInfo
                            ? ReflectionHelper.GetValue(model, idMemberInfo)
                            : null;
                        if (modelId is not null)
                            await repository.TryFindOneAsync(modelId).ConfigureAwait(false);
                    }
                }
            }
        }

        public void ClearChangeCandidate(IEntityModel model)
        {
            ArgumentNullException.ThrowIfNull(model);

            lock (trackingLock)
                changeCandidates.Remove(model);
        }

        public void MarkChangeCandidate(IEntityModel model)
        {
            ArgumentNullException.ThrowIfNull(model);

            bool marked;
            lock (trackingLock)
            {
                /* Ignore the mark until the model has a model document: the member sets replayed
                 * while deserializing run before the model document capture, and must not be tracked. Ignore
                 * it while merging loaded data into a model too, keeping the merges out of the
                 * unit of work. */
                if (changeTrackingSuppressions > 0 || !modelBsonDocuments.ContainsKey(model))
                    return;
                marked = changeCandidates.Add(model);
            }

            if (marked &&
                TryGetSourceRepository(model) is { } repository)
                logger.DbContextRegisteredChangedModel(engine.Options.DbName, repository.ModelIdToString(model), repository.Name);
        }

        public void OnImplicitLazyLoad(Type modelType, string? memberName)
        {
            ArgumentNullException.ThrowIfNull(modelType);

            switch (engine.Options.ImplicitLazyLoad)
            {
                case ImplicitLazyLoadMode.Silent:
                    break;

                case ImplicitLazyLoadMode.Throw:
                    throw new MongodmLazyLoadingException(
                        $"Denied implicit lazy load on model type {modelType.Name}" +
                        (memberName is null ? " from a domain method" : $", member {memberName}") +
                        $": preload members with {nameof(LoadValuesAsync)}, or allow implicit lazy loads on the db context options");

                default:
                    bool firstOccurrence;
                    lock (trackingLock)
                        firstOccurrence = warnedImplicitLazyLoads.Add((modelType, memberName));
                    if (firstOccurrence)
                        logger.DbContextImplicitLazyLoad(engine.Options.DbName, modelType.Name, memberName);
                    break;
            }
        }

        public void RegisterLoadedModel(object modelId, IEntityModel model)
        {
            ArgumentNullException.ThrowIfNull(modelId);
            ArgumentNullException.ThrowIfNull(model);

            var repository = TryGetSourceRepository(model);
            if (repository is null) //identity is meaningless without a repository
                return;

            lock (loadedModels)
                loadedModels[(repository, modelId)] = model;

            logger.DbContextRegisteredLoadedModel(engine.Options.DbName, modelId.ToString()!, repository.Name);
        }

        public void ReplaceOutdatedLoadedModel(object modelId, IEntityModel outdatedModel, IEntityModel currentModel)
        {
            ArgumentNullException.ThrowIfNull(modelId);
            ArgumentNullException.ThrowIfNull(outdatedModel);
            ArgumentNullException.ThrowIfNull(currentModel);

            // Validate that both instances belong to the identified document, before any state mutation.
            /* The id reads stay legal on an invalidated instance: the id member is not
             * proxied, being definitionally present and immutable. */
            ValidateModelId(modelId, outdatedModel, nameof(outdatedModel));
            ValidateModelId(modelId, currentModel, nameof(currentModel));

            /* The runtime type of the outdated instance can't upgrade: flag it, so any
             * application interaction with it fails loudly instead of proceeding with the
             * wrong type, and drop it from the change tracking. The fresh instance becomes
             * the loaded one for the document, served by the next loads. */
            var currentModelType = engine.ProxyGenerator.PurgeProxyType(currentModel.GetType());
            (outdatedModel as IProxyModel)?.SetOutdatedModelType(currentModelType);
            RemoveModelTracking(outdatedModel);
            RegisterLoadedModel(modelId, currentModel);

            logger.DbContextReplacedOutdatedLoadedModel(
                engine.Options.DbName,
                modelId.ToString()!,
                engine.ProxyGenerator.PurgeProxyType(outdatedModel.GetType()).Name,
                currentModelType.Name);
        }

        public virtual async Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            // Commit updated models replacement.
            /* The models to save are the change candidates flagged by proxy mutations, plus every
             * non proxy tracked model: a non proxy instance can't self signal its mutations, so
             * it's always diffed against its model document. Diffs with no change save nothing. */
            var modelsToSave = GetModelsToSave();
            logger.DbContextSavingChanges(engine.Options.DbName, modelsToSave.Count);

            /* When transactions are enabled by options and supported by the connected
             * deployment, the changed models save into a single implicit transaction:
             * partial saves can't survive a failure. Skip the new transaction when a
             * session is already ambient, enlisting in it instead of nesting. Child db
             * contexts stay out in any case: they save on their own connections, each
             * applying its own configuration. */
            if (engine.Options.EnableTransactionsWithReplicaSet &&
                modelsToSave.Count > 0 &&
                engine.SupportsTransactions &&
                DbSessionHandler.TryGetCurrentSession(engine) is null)
            {
                await ExecuteInTransactionAsync(
                    () => SaveChangedModelsAsync(modelsToSave, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await SaveChangedModelsAsync(modelsToSave, cancellationToken).ConfigureAwait(false);
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
            // Skip on a read-only db context: seeding and migrations belong to the db owner.
            if (engine.Options.IsReadOnly)
            {
                logger.DbContextSeedingSkippedOnReadOnly(engine.Options.DbName);
                return false;
            }

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

        public IEntityModel? TryGetLoadedModel(IRepository repository, object modelId)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentNullException.ThrowIfNull(modelId);

            IEntityModel? model;
            lock (loadedModels)
                loadedModels.TryGetValue((repository, modelId), out model);

            if (model is not null)
                logger.DbContextReturnedLoadedModel(engine.Options.DbName, modelId.ToString()!, repository.Name);

            return model;
        }

        public IEntityModel? TryGetLoadedModel(Type modelType, object modelId)
        {
            ArgumentNullException.ThrowIfNull(modelType);
            ArgumentNullException.ThrowIfNull(modelId);

            var repository = TryGetRepositoryForModelType(modelType);
            if (repository is null)
                return null;

            return TryGetLoadedModel(repository, modelId);
        }

        public Task<DbMigrationOperation?> TryStartMigrationAsync() =>
            engine.DbMigrationManager.TryStartDbContextMigrationAsync(this);

        public void RemoveModelTracking(IEntityModel model)
        {
            ArgumentNullException.ThrowIfNull(model);

            bool removed;
            lock (trackingLock)
            {
                changeCandidates.Remove(model);
                modelSourceRepositories.Remove(model);
                removed = modelBsonDocuments.Remove(model);
            }

            if (removed &&
                TryGetSourceRepository(model) is { } repository)
                logger.DbContextUnregisteredChangedModel(engine.Options.DbName, repository.ModelIdToString(model), repository.Name);
        }

        public void SetModelBsonDocument(IEntityModel model, BsonDocument bsonDocument)
        {
            ArgumentNullException.ThrowIfNull(model);
            ArgumentNullException.ThrowIfNull(bsonDocument);

            lock (trackingLock)
                modelBsonDocuments[model] = bsonDocument;
        }

        public void SetModelSourceRepository(IEntityModel model, IRepository sourceRepository)
        {
            ArgumentNullException.ThrowIfNull(model);
            ArgumentNullException.ThrowIfNull(sourceRepository);

            lock (trackingLock)
                modelSourceRepositories[model] = sourceRepository;
        }

        public IDisposable SuppressChangeTracking()
        {
            lock (trackingLock)
                changeTrackingSuppressions++;
            return new ChangeTrackingSuppression(this);
        }

        public BsonDocument? TryGetModelBsonDocument(IEntityModel model)
        {
            ArgumentNullException.ThrowIfNull(model);

            lock (trackingLock)
                return modelBsonDocuments.GetValueOrDefault(model);
        }

        public void UnregisterLoadedModel(object modelId, IEntityModel model)
        {
            ArgumentNullException.ThrowIfNull(modelId);
            ArgumentNullException.ThrowIfNull(model);

            var repository = TryGetSourceRepository(model);
            if (repository is null)
                return;

            bool unregistered = false;
            lock (loadedModels)
            {
                //remove only if this same instance is the registered one
                if (loadedModels.TryGetValue((repository, modelId), out var loadedModel) &&
                    ReferenceEquals(loadedModel, model))
                    unregistered = loadedModels.Remove((repository, modelId));
            }

            if (unregistered)
                logger.DbContextUnregisteredLoadedModel(engine.Options.DbName, modelId.ToString()!, repository.Name);
        }

        // Protected methods.
        protected virtual Task SeedAsync() =>
            Task.CompletedTask;

        // Helpers.
        private List<IEntityModel> GetModelsToSave()
        {
            var modelsToSave = new HashSet<object>(ReferenceEqualityComparer.Instance);
            lock (trackingLock)
            {
                foreach (var candidate in changeCandidates)
                    modelsToSave.Add(candidate);

                //non proxy tracked models can't self signal their mutations: always diff them.
                foreach (var model in modelBsonDocuments.Keys)
                    if (!engine.ProxyGenerator.IsProxyType(model.GetType()))
                        modelsToSave.Add(model);
            }
            return modelsToSave.Cast<IEntityModel>().ToList();
        }

        private async Task SaveChangedModelsAsync(
            IReadOnlyCollection<IEntityModel> changedModelsList,
            CancellationToken cancellationToken)
        {
            foreach (var model in changedModelsList)
            {
                var repository = TryGetSourceRepository(model);
                if (repository != null)
                {
                    await repository.SaveChangesAsync(model, cancellationToken).ConfigureAwait(false);

                    logger.DbContextSavedChangedModelToRepository(engine.Options.DbName, repository.ModelIdToString(model), repository.Name);
                }
            }
        }

        private IRepository? TryGetSourceRepository(IEntityModel model)
        {
            //a referenceable model carries its bound source; a tracked non proxy model (created
            //or replaced) carries the repository that handled it; else resolve by model type.
            if ((model as IReferenceable)?.SourceRepository is { } referenceableRepository)
                return referenceableRepository;

            lock (trackingLock)
                if (modelSourceRepositories.TryGetValue(model, out var trackedRepository))
                    return trackedRepository;

            return TryGetRepositoryForModelType(engine.ProxyGenerator.PurgeProxyType(model.GetType()));
        }

        private IRepository? TryGetRepositoryForModelType(Type modelType) =>
            scopedRepositoryRegistry?.TryGetRepositoryByHandledModelType(modelType);

        private MemberInfo? TryGetIdMemberInfo(Type modelType)
        {
            /* The identity member is the mapped id of the model map active schema, not
             * necessarily a property named "Id". Any working entity map resolves one:
             * explicitly mapped, auto mapped by the driver conventions, or inherited
             * from the linked base maps. Null only for unmapped model types. */
            if (engine.MapRegistry.TryGetModelMap(engine.ProxyGenerator.PurgeProxyType(modelType), out var modelMap) &&
                modelMap.ActiveSchema.AllMemberMaps.FirstOrDefault(mm => mm.IsIdMember())?.MemberInfo is { } idMemberInfo)
                return idMemberInfo;
            return null;
        }

        private void ValidateModelId(object modelId, IEntityModel model, string paramName)
        {
            var idMemberInfo = TryGetIdMemberInfo(model.GetType())
                ?? throw new InvalidOperationException(
                    $"Can't resolve the mapped id member of model type {engine.ProxyGenerator.PurgeProxyType(model.GetType()).Name}");
            var modelIdValue = ReflectionHelper.GetValue(model, idMemberInfo);
            if (!modelId.Equals(modelIdValue))
                throw new ArgumentException(
                    $"Model id {modelIdValue ?? "null"} doesn't match the document id {modelId}", paramName);
        }

        // Nested types.
        private sealed class ChangeTrackingSuppression(DbContext dbContext) : IDisposable
        {
            public void Dispose()
            {
                lock (dbContext.trackingLock)
                    dbContext.changeTrackingSuppressions--;
            }
        }
    }
}
