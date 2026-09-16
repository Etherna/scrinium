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
using Etherna.MongoDB.Driver;
using Etherna.MongoDB.Driver.Linq;
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Exceptions;
using Etherna.Scrinium.Core.Extensions;
using Etherna.Scrinium.Core.Migration;
using Etherna.Scrinium.Core.Options;
using Etherna.Scrinium.Core.ProxyModels;
using Etherna.Scrinium.Core.Repositories;
using Etherna.Scrinium.Core.Serialization;
using Etherna.Scrinium.Core.Serialization.Mapping;
using Etherna.Scrinium.Core.Utility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.Scrinium.Core
{
    [SuppressMessage("Design", "CA1033:Interface methods should be callable by child types",
        Justification = "The explicitly implemented members are infrastructure invoked by the generated proxy models, deliberately out of the surface of derived db contexts")]
    public abstract class DbContext(ILogger? logger = null)
        : IDbContext, IDbContextBuilder, IInternalDbContext
    {
        // Consts.
        private const int SeedingLockMinRetries = 4;
        private static readonly TimeSpan SeedingLockRetryDelay = TimeSpan.FromSeconds(5);
        /* The transient failures retry with the driver policy of its transactions callback
         * api: an exponential backoff with full jitter between the attempts, from 5 ms up
         * to 500 ms, and a commit retried on an unknown result. */
        private const double TransactionRetryBackoffBase = 1.5;
        private const double TransactionRetryBackoffInitialMs = 5;
        private const double TransactionRetryBackoffMaxMs = 500;
        private const string TransientTransactionErrorLabel = "TransientTransactionError";
        private const string UnknownTransactionCommitResultLabel = "UnknownTransactionCommitResult";

        // Fields.
        /* Change tracking state keyed by reference identity: a model is tracked by its
         * serialized model document captured at load, and a proxy signals its mutations marking
         * itself a change candidate. Non proxy tracked models can't self signal, so they are all
         * diffed at save. EntityModelBase equates by id, so an id based comparer would collapse
         * distinct instances: identity is the required key here. */
        private readonly HashSet<object> changeCandidates = new(ReferenceEqualityComparer.Instance);
        private int changeTrackingSuppressions;
        private IEnumerable<IDbContext>? childDbContexts;
        private IDbContextEngine engine = null!;
        private readonly Dictionary<(IRepository Repository, object ModelId), IEntityModel> loadedModels = [];
        private readonly ILogger logger = logger ?? NullLogger.Instance;
        private readonly Dictionary<object, BsonDocument> modelBsonDocuments = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, IRepository> modelSourceRepositories = new(ReferenceEqualityComparer.Instance);
        private IRepositoryRegistry? scopedRepositoryRegistry;
        private readonly object trackingLock = new();
        //the transient models scopes open on the flow, collecting the models entering while they run
        private readonly List<TransientModelsScope> transientModelsScopes = [];
        private readonly HashSet<(Type ModelType, string? MemberName)> warnedImplicitLazyLoads = [];
        private readonly HashSet<(Type ModelType, IRepository SourceRepository)> warnedMissingOriginDocuments = [];

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
        public IEnumerable<IDbContext> ChildDbContexts => childDbContexts ?? [];
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

            /* A transaction failing with the transient error label (a write conflict with a
             * concurrent transaction, a primary election) is expected to be retried by the
             * application: the server asks for it, and the driver transactions callback api
             * retries the callback the same way. The whole function replays on a new
             * transaction of the session, inside the retry budget measured from the first
             * start, with the unit of work as the aborted attempt left it: the enlisted saves
             * still pending, and the enlisted creates undone. */
            var retryStopwatch = Stopwatch.StartNew();
            for (var attempt = 1; ; attempt++)
            {
                session.StartTransaction();
                logger.DbContextStartedTransaction(engine.Options.DbName);

                /* The session handler enlists in the transaction every operation invoked
                 * without an explicit session on collections of this engine, for the whole
                 * function execution, and collects the bookkeeping they defer to its outcome. */
                using var sessionHandler = new DbSessionHandler(engine, session);

                TResult result;
                try
                {
                    result = await func().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    /* Abort with an uncancellable token: the function may have thrown for the
                     * cancellation itself, and the abort must run anyway. The undo deferred to
                     * the abort runs, and the bookkeeping deferred to the commit drops: the
                     * tracking state returns to the one before the transaction, with the
                     * enlisted saves still pending. */
                    await session.AbortTransactionAsync(CancellationToken.None).ConfigureAwait(false);
                    logger.DbContextAbortedTransaction(engine.Options.DbName);
                    sessionHandler.RunAbortActions();

                    if (!TryGetTransactionRetryDelay(e, attempt, retryStopwatch.Elapsed, cancellationToken, out var retryDelay))
                        throw;
                    logger.DbContextRetryingTransaction(engine.Options.DbName, attempt, e);
                    await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    await CommitTransactionWithRetriesAsync(session, retryStopwatch, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    /* A failed commit leaves the session out of the transaction: the server
                     * aborted it, or may have committed it past the unknown result retries.
                     * The in memory effects undo like at an abort, and a transient failure
                     * restarts the transaction on the session, with no abort of its own. */
                    sessionHandler.RunAbortActions();

                    if (!TryGetTransactionRetryDelay(e, attempt, retryStopwatch.Elapsed, cancellationToken, out var retryDelay))
                        throw;
                    logger.DbContextRetryingTransaction(engine.Options.DbName, attempt, e);
                    await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                logger.DbContextCommittedTransaction(engine.Options.DbName);

                /* The tracking state follows the commit: the saves enlisted in the transaction
                 * refresh their models and clear their change candidates only now, so an abort
                 * leaves every model as it was before the transaction, still tracked and still dirty. */
                sessionHandler.RunCommitActions();

                return result;
            }
        }

        public Task ExecuteMigrationAsync(string dbMigrationOpId, string? taskId = null, bool throwOnErrors = false) =>
            engine.DbMigrationManager.ExecuteDbContextMigrationAsync(this, dbMigrationOpId, taskId, throwOnErrors);

        public Task ExecuteReferencesRepairAsync(string referencesRepairOpId, string? taskId = null, bool throwOnErrors = false) =>
            engine.ReferencesRepairManager.ExecuteReferencesRepairAsync(this, referencesRepairOpId, taskId, throwOnErrors);

        public Task<List<DbMigrationOperation>> GetLastMigrationsAsync(int page, int take) =>
            engine.DbMigrationManager.GetLastMigrationsAsync(this, page, take);

        public Task<DbMigrationOperation> GetMigrationAsync(string migrateOperationId) =>
            engine.DbMigrationManager.GetMigrationAsync(this, migrateOperationId);

        /* The operations of every kind, read without a manager of their own: no single kind
         * owns this, and the query is the plain paginated read of the operations collection.
         * It runs with exclusive access allowance, like the per kind reads do, so it keeps
         * working while an operation is locking the db context. */
        public async Task<List<OperationBase>> GetLastOperationsAsync(int page, int take)
        {
            using var exclusiveAccess = new ExclusiveAccessHandler(engine);

            // Paginate on Id: ObjectId ids embed the creation instant.
            return await DbOperations.QueryElementsAsync(elements =>
                elements.Where(op => op.DbContextName == engine.Identifier)
                        .PaginateDescending(r => r.Id, page, take)
                        .ToListAsync()).ConfigureAwait(false);
        }

        public Task<List<ReferencesRepairOperation>> GetLastReferencesRepairsAsync(int page, int take) =>
            engine.ReferencesRepairManager.GetLastReferencesRepairsAsync(this, page, take);

        public Task<ReferencesRepairOperation> GetReferencesRepairAsync(string referencesRepairOpId) =>
            engine.ReferencesRepairManager.GetReferencesRepairAsync(this, referencesRepairOpId);

        public Task<DbMigrationOperation?> IsMigrationRunningAsync() =>
            engine.DbMigrationManager.IsMigrationRunningAsync(this);

        public Task<ReferencesRepairOperation?> IsReferencesRepairRunningAsync() =>
            engine.ReferencesRepairManager.IsReferencesRepairRunningAsync(this);

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

        public Task<bool> IsResourceLockedAsync(string resourceNamespace, string resourceId) =>
            engine.GetResourceLock(resourceNamespace, resourceId).IsLockedAsync();

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
            List<(IEntityModel Model, IRepository Repository, object ModelId)> modelsToLoad = [];
            foreach (var model in models)
            {
                if (model is not IReferenceable { IsSummary: true } referenceable)
                    continue;

                var idMemberInfo = TryGetIdMemberInfo(model.GetType());

                //the id member is definitionally present, so it never requires a load
                var loadedMemberNames = referenceable.SettedMemberNames.ToHashSet(StringComparer.Ordinal);
                if (memberNames.All(name =>
                        loadedMemberNames.Contains(name) ||
                        idMemberInfo?.Name == name))
                    continue;

                //a model without an id addresses no document to load
                if (idMemberInfo is null ||
                    ReflectionHelper.GetValue(model, idMemberInfo) is not { } modelId)
                    continue;

                modelsToLoad.Add((model, referenceable.SourceRepository, modelId));
            }

            /* One batched load per source repository: the loaded documents deserialize on
             * the scope owning the repository, merging in place into the instances registered
             * on its identity map. Custom repository implementations without the batch
             * surface load per instance. */
            foreach (var repositoryGroup in modelsToLoad.GroupBy(pair => pair.Repository))
            {
                IReadOnlyDictionary<object, IEntityModel> loadedModels;
                if (repositoryGroup.Key is IFullModelsLoader fullModelsLoader)
                {
                    loadedModels = await fullModelsLoader.LoadFullModelsAsync(
                        repositoryGroup.Select(pair => pair.Model)).ConfigureAwait(false);
                }
                else
                {
                    Dictionary<object, IEntityModel> foundModels = [];
                    foreach (var (_, repository, modelId) in repositoryGroup)
                        if (await repository.TryFindOneAsync(modelId).ConfigureAwait(false) is IEntityModel foundModel)
                            foundModels[modelId] = foundModel;
                    loadedModels = foundModels;
                }

                foreach (var (model, _, modelId) in repositoryGroup)
                {
                    /* A requested instance registered on the identity map merged in place,
                     * giving up the summary state; one invalidated by a document type change
                     * is outdated, not missing: its fresh instance replaced it as the loaded
                     * model. */
                    if (model is not IReferenceable { IsSummary: true } referenceable ||
                        model is IProxyModel { OutdatedModelType: not null })
                        continue;

                    /* A requested instance not registered on the identity map (deserialized
                     * out of it, or evicted from it) is still a summary after the load merged
                     * into the registered one: upgrade it from the loaded instance, so the
                     * preload upgrades what it received, invalidating it instead when the
                     * document carries another type, like the identity map invalidates the
                     * registered instance. */
                    if (loadedModels.TryGetValue(modelId, out var loadedModel))
                    {
                        var loadedModelType = engine.ProxyGenerator.PurgeProxyType(loadedModel.GetType());
                        if (loadedModelType == engine.ProxyGenerator.PurgeProxyType(model.GetType()))
                            referenceable.MergeFullModel(loadedModel);
                        else
                            (model as IProxyModel)?.SetOutdatedModelType(loadedModelType);
                    }
                    else
                    {
                        //the load found no document: the origin document doesn't exist anymore
                        ((IProxyModelsDbContext)this).OnMissingOriginDocument(model);
                    }
                }
            }
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
            foreach (var child in ChildDbContexts)
            {
                await child.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            logger.DbContextSavedChanges(engine.Options.DbName);
        }

        public async Task<bool> SeedIfNeededAsync(TimeSpan? lockWaitTimeout = null, TimeSpan? lockLeaseDuration = null)
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

            // Claim the db context lock before entering the exclusive window: seeding must run
            // once per db context across every application instance connected to the database,
            // and the in-process exclusive access can't exclude the other processes.
            /* While another owner holds the lock it may be seeding this same database: wait
             * re-reading the seeding state from the db, instead of seeding again. A dead owner
             * stops renewing its lease, whose expiration unblocks the claim. */
            var lockOwnerId = Guid.NewGuid().ToString();
            var effectiveLockLeaseDuration = lockLeaseDuration ?? ResourceLock.DefaultLeaseDuration;
            /* Without an explicit wait, the lease duration of this seeding bounds it: a dead
             * owner's lease always expires inside it, so only a live owner working longer fails
             * the seeding. */
            var effectiveLockWaitTimeout = lockWaitTimeout ?? effectiveLockLeaseDuration;
            //retry with the standard delay, shortened when the wait admits less retries
            var lockRetryDelay = TimeSpan.FromTicks(Math.Min(
                SeedingLockRetryDelay.Ticks,
                effectiveLockWaitTimeout.Ticks / SeedingLockMinRetries));
            var lockWaitStopwatch = Stopwatch.StartNew();
            while (!await engine.DbContextLock.TryClaimAsync(lockOwnerId, effectiveLockLeaseDuration).ConfigureAwait(false))
            {
                /* The wait is bounded: the caller blocks on the seeding (the startup one waits
                 * on every db context of the application), so an owner never releasing the
                 * lock must fail this seeding instead of hanging it forever. */
                if (lockWaitStopwatch.Elapsed >= effectiveLockWaitTimeout)
                    throw new ScriniumDbSeedingException(
                        $"Can't seed {GetType().Name} dbContext: another owner held the db context lock " +
                        $"for more than the {effectiveLockWaitTimeout} wait timeout");

                logger.DbContextSeedingWaitingForLock(engine.Options.DbName);
                await Task.Delay(lockRetryDelay).ConfigureAwait(false);

                engine.IsSeededCache = null; //not seeded may be cached: re-read from db
                if (IsSeeded)
                    return false;
            }

            // Resume the claim into a renewed lease, releasing the claim if it can't be resumed.
            /* A claim nobody owns would deny every seeding and migration of the db context,
             * on every application instance, until its lease expiration. */
            IResourceLockLease lockLease;
            try
            {
                lockLease = await engine.DbContextLock.TryResumeClaimAsync(lockOwnerId).ConfigureAwait(false)
                    ?? throw new InvalidOperationException(
                        $"Can't resume the just claimed db context lock, seeding {GetType().Name} dbContext");
            }
            catch
            {
                await engine.DbContextLock.TryReleaseAsync(lockOwnerId).ConfigureAwait(false);
                throw;
            }

            try
            {
                return await engine.RunWithExclusiveAccessAsync(async () =>
                {
                    // Check again if seeded: the claim serializes the seeders across processes,
                    // but another one may have completed before the claim.
                    engine.IsSeededCache = null;
                    if (IsSeeded)
                        return false;

                    // Apply db migration, blocking seed in case of errors.
                    // This creates indexes by default on each new database.
                    var dbMigrationOp = new DbMigrationOperation(engine);
                    await DbOperations.CreateAsync(dbMigrationOp).ConfigureAwait(false);
                    await ExecuteMigrationAsync(dbMigrationOp.Id, throwOnErrors: true).ConfigureAwait(false);

                    // Seed.
                    try { await SeedAsync().ConfigureAwait(false); }
                    catch (Exception e) { throw new ScriniumDbSeedingException($"Error seeding {GetType().Name} dbContext", e); }

                    // Report operation.
                    var seedOperation = new SeedOperation(engine);
                    await DbOperations.CreateAsync(seedOperation).ConfigureAwait(false);

                    // Cache as seeded.
                    engine.IsSeededCache = true;

                    logger.DbContextSeeded(engine.Options.DbName);

                    return true;
                }).ConfigureAwait(false);
            }
            finally
            {
                await lockLease.DisposeAsync().ConfigureAwait(false);
            }
        }

        public Task<IResourceLockLease?> TryAcquireResourceLockAsync(
            string resourceNamespace,
            string resourceId,
            ResourceLockMode mode = ResourceLockMode.Exclusive,
            TimeSpan? leaseDuration = null) =>
            engine.GetResourceLock(resourceNamespace, resourceId).TryAcquireAsync(mode, leaseDuration);

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

        public Task<DbMigrationOperation?> TryStartMigrationAsync(
            bool dryRun = false,
            bool stopAtFirstError = false,
            TimeSpan? lockLeaseDuration = null,
            bool rewriteDeprecatedSchemas = false) =>
            engine.DbMigrationManager.TryStartDbContextMigrationAsync(
                this, dryRun, stopAtFirstError, lockLeaseDuration, rewriteDeprecatedSchemas);

        public Task<ReferencesRepairOperation?> TryStartReferencesRepairAsync(
            string repositoryName,
            IReadOnlyDictionary<string, OriginDeleteMode> repairModesByElementPath,
            bool dryRun = false,
            TimeSpan? lockLeaseDuration = null) =>
            engine.ReferencesRepairManager.TryStartReferencesRepairAsync(
                this, repositoryName, repairModesByElementPath, dryRun, lockLeaseDuration);

        public IDisposable StartTransientModelsScope()
        {
            /* An open scope collects what the flow registers while it runs, and evicts exactly
             * that at its dispose: the models entered before keep their state, updates applied
             * inside the scope included. Collecting the entering models, instead of capturing
             * the entered ones, keeps the cost of a scope proportional to what it evicts and
             * not to what the db context already holds. */
            var scope = new TransientModelsScope(this);
            lock (transientModelsScopes)
                transientModelsScopes.Add(scope);
            return scope;
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

        // Internals.
        void IInternalDbContext.ClearChangeCandidate(IEntityModel model)
        {
            ArgumentNullException.ThrowIfNull(model);

            lock (trackingLock)
                changeCandidates.Remove(model);
        }

        void IInternalDbContext.RegisterLoadedModel(object modelId, IEntityModel model)
        {
            ArgumentNullException.ThrowIfNull(modelId);
            ArgumentNullException.ThrowIfNull(model);

            var repository = TryGetSourceRepository(model);
            if (repository is null) //identity is meaningless without a repository
                return;

            lock (loadedModels)
            {
                /* Report only a model entering the identity map: an already loaded key belongs
                 * to whoever entered it, and a scope registering it again doesn't own it. */
                var loadedModelKey = (repository, modelId);
                if (!loadedModels.ContainsKey(loadedModelKey))
                    ReportToTransientModelsScopes(scope => scope.ReportLoadedModel(loadedModelKey));

                loadedModels[loadedModelKey] = model;
            }

            logger.DbContextRegisteredLoadedModel(engine.Options.DbName, modelId.ToString()!, repository.Name);
        }

        void IInternalDbContext.RemoveModelTracking(IEntityModel model)
        {
            ArgumentNullException.ThrowIfNull(model);

            bool removed;
            IRepository? trackedRepository;
            lock (trackingLock)
            {
                changeCandidates.Remove(model);
                modelSourceRepositories.Remove(model, out trackedRepository);
                removed = modelBsonDocuments.Remove(model);
            }

            /* A created instance carries its source repository only in the dropped binding:
             * resolving it by model type would fail on a type handled by many repositories. */
            if (removed &&
                (trackedRepository ?? TryGetSourceRepository(model)) is { } repository)
                logger.DbContextUnregisteredChangedModel(engine.Options.DbName, repository.ModelIdToString(model), repository.Name);
        }

        void IInternalDbContext.ReplaceOutdatedLoadedModel(object modelId, IEntityModel outdatedModel, IEntityModel currentModel)
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
            var internalDbContext = (IInternalDbContext)this;
            internalDbContext.RemoveModelTracking(outdatedModel);
            internalDbContext.RegisterLoadedModel(modelId, currentModel);

            logger.DbContextReplacedOutdatedLoadedModel(
                engine.Options.DbName,
                modelId.ToString()!,
                engine.ProxyGenerator.PurgeProxyType(outdatedModel.GetType()).Name,
                currentModelType.Name);
        }

        void IInternalDbContext.SetModelBsonDocument(IEntityModel model, BsonDocument bsonDocument)
        {
            ArgumentNullException.ThrowIfNull(model);
            ArgumentNullException.ThrowIfNull(bsonDocument);

            lock (trackingLock)
            {
                //report only a model entering the tracking: a model document update doesn't own it
                if (!modelBsonDocuments.ContainsKey(model))
                    ReportToTransientModelsScopes(scope => scope.ReportTrackedModel(model));

                modelBsonDocuments[model] = bsonDocument;
            }
        }

        void IInternalDbContext.SetModelSourceRepository(IEntityModel model, IRepository sourceRepository)
        {
            ArgumentNullException.ThrowIfNull(model);
            ArgumentNullException.ThrowIfNull(sourceRepository);

            lock (trackingLock)
                modelSourceRepositories[model] = sourceRepository;
        }

        BsonDocument? IInternalDbContext.TryGetModelBsonDocument(IEntityModel model)
        {
            ArgumentNullException.ThrowIfNull(model);

            lock (trackingLock)
                return modelBsonDocuments.GetValueOrDefault(model);
        }

        bool IProxyModelsDbContext.IsChangeTrackingSuppressed
        {
            get
            {
                lock (trackingLock)
                    return changeTrackingSuppressions > 0;
            }
        }

        void IProxyModelsDbContext.MarkChangeCandidate(IEntityModel model)
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

        void IProxyModelsDbContext.OnImplicitLazyLoad(Type modelType, string? memberName)
        {
            ArgumentNullException.ThrowIfNull(modelType);

            switch (engine.Options.ImplicitLazyLoad)
            {
                case ReactionMode.Silent:
                    break;

                case ReactionMode.Throw:
                    throw new ScriniumLazyLoadingException(
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

        void IProxyModelsDbContext.OnMissingOriginDocument(IEntityModel summaryModel)
        {
            ArgumentNullException.ThrowIfNull(summaryModel);

            if (summaryModel is not IReferenceable referenceable)
                return;

            var modelType = engine.ProxyGenerator.PurgeProxyType(summaryModel.GetType());
            var sourceRepository = referenceable.SourceRepository;

            switch (referenceable.MissingOriginDocument)
            {
                case ReactionMode.Silent:
                    break;

                case ReactionMode.Warn:
                    /* Report the model type and its source repository, without the document id:
                     * an id can be a natural identifier, and this level is enabled by default. */
                    bool firstOccurrence;
                    lock (trackingLock)
                        firstOccurrence = warnedMissingOriginDocuments.Add((modelType, sourceRepository));
                    if (firstOccurrence)
                        logger.DbContextMissingOriginDocument(engine.Options.DbName, modelType.Name, sourceRepository.Name);
                    break;

                default:
                    var modelId = TryGetIdMemberInfo(summaryModel.GetType()) is { } idMemberInfo
                        ? ReflectionHelper.GetValue(summaryModel, idMemberInfo)
                        : null;
                    throw new ScriniumMissingOriginDocumentException(
                        $"Summary model of type {modelType.Name} with id {modelId ?? "null"} has no origin document " +
                        $"on repository {sourceRepository.Name}: the referred document doesn't exist on its collection, " +
                        "and its members can't be loaded. Fix the db inconsistency, or configure the reference to " +
                        "tolerate a missing origin document");
            }
        }

        IDisposable IProxyModelsDbContext.SuppressChangeTracking()
        {
            lock (trackingLock)
                changeTrackingSuppressions++;
            return new ChangeTrackingSuppression(this);
        }

        // Helpers.
        private async Task CommitTransactionWithRetriesAsync(
            IClientSessionHandle session,
            Stopwatch retryStopwatch,
            CancellationToken cancellationToken)
        {
            /* A commit of unknown result (a connection lost while committing, for instance)
             * retries on the same transaction, like the driver transactions callback api: the
             * server applies a repeated commit once. The api excludes the commits expired on
             * their max commit time, an option the transactions started here never set. */
            while (true)
            {
                try
                {
                    await session.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (MongoException e) when (
                    e.HasErrorLabel(UnknownTransactionCommitResultLabel) &&
                    !cancellationToken.IsCancellationRequested &&
                    retryStopwatch.Elapsed < engine.Options.TransactionRetryTimeout)
                {
                    logger.DbContextRetryingTransactionCommit(engine.Options.DbName, e);
                }
            }
        }

        private void ReportToTransientModelsScopes(Action<TransientModelsScope> report)
        {
            /* Report to every open scope, not only to the innermost: a model entering inside a
             * nested scope belongs to the outer ones too, whose eviction of an already evicted
             * model is a no-op. */
            lock (transientModelsScopes)
                foreach (var scope in transientModelsScopes)
                    report(scope);
        }

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
            //a proxy model carries its bound source; a tracked non proxy model (created
            //or replaced) carries the repository that handled it; else resolve by model type.
            if (model is IReferenceable referenceable)
                return referenceable.SourceRepository;

            lock (trackingLock)
                if (modelSourceRepositories.TryGetValue(model, out var trackedRepository))
                    return trackedRepository;

            return scopedRepositoryRegistry?.TryGetRepositoryByHandledModelType(
                engine.ProxyGenerator.PurgeProxyType(model.GetType()));
        }

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

        [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
            Justification = "The jitter only spreads the retries of concurrent transactions: no security relies on it")]
        private bool TryGetTransactionRetryDelay(
            Exception exception,
            int attempt,
            TimeSpan elapsed,
            CancellationToken cancellationToken,
            out TimeSpan retryDelay)
        {
            /* Only the failures the server labels as transient retry, and only while the
             * caller still waits: a cancelled flow gives up with its own failure. The backoff
             * grows with the failed attempts, and the budget bounds the next attempt start. */
            retryDelay = TimeSpan.Zero;
            if (exception is not MongoException mongoException ||
                !mongoException.HasErrorLabel(TransientTransactionErrorLabel) ||
                cancellationToken.IsCancellationRequested)
                return false;

            retryDelay = TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * Math.Min(
                TransactionRetryBackoffMaxMs,
                TransactionRetryBackoffInitialMs * Math.Pow(TransactionRetryBackoffBase, attempt)));
            return elapsed + retryDelay < engine.Options.TransactionRetryTimeout;
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

        private sealed class TransientModelsScope(DbContext dbContext) : IDisposable
        {
            // Fields.
            private readonly HashSet<(IRepository Repository, object ModelId)> enteredLoadedModelsKeys = [];
            private readonly HashSet<object> enteredTrackedModels = new(ReferenceEqualityComparer.Instance);

            // Methods.
            public void Dispose()
            {
                lock (dbContext.transientModelsScopes)
                    if (!dbContext.transientModelsScopes.Remove(this))
                        return; //already disposed

                // Evict the models registered as loaded inside the scope.
                int evictedLoadedModelsCount = 0;
                lock (dbContext.loadedModels)
                    foreach (var key in enteredLoadedModelsKeys)
                        if (dbContext.loadedModels.Remove(key))
                            evictedLoadedModelsCount++;

                // Evict the tracking of the models tracked inside the scope.
                /* A change candidate always carries a model document, so untracking the models
                 * entered here drops their candidate flags too. A candidate flagged inside the
                 * scope on a model tracked before it stays: its change is real application
                 * state, saved by the next changes save. */
                foreach (var model in enteredTrackedModels.Cast<IEntityModel>())
                    ((IInternalDbContext)dbContext).RemoveModelTracking(model);

                dbContext.logger.DbContextEvictedTransientModels(
                    dbContext.engine.Options.DbName,
                    evictedLoadedModelsCount,
                    enteredTrackedModels.Count);
            }

            //reported under the lock guarding the state they enter
            public void ReportLoadedModel((IRepository Repository, object ModelId) key) =>
                enteredLoadedModelsKeys.Add(key);

            public void ReportTrackedModel(object model) =>
                enteredTrackedModels.Add(model);
        }
    }
}
