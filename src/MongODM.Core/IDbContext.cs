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
using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.Migration;
using Etherna.MongODM.Core.Repositories;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core
{
    /// <summary>
    /// Interface of <see cref="DbContext"/> implementation. The unit of work over
    /// a scope independent <see cref="IDbContextEngine"/>.
    /// </summary>
    public interface IDbContext
    {
        // Properties.
        /// <summary>
        /// List of proxy models flagged as change candidates by their mutations on this db
        /// context instance. The actual changes are computed at save by diffing each model
        /// serialization against its model document; non proxy tracked models don't appear here, but
        /// their changes are still saved.
        /// </summary>
        IReadOnlyCollection<IEntityModel> ChangedModelsList { get; }

        /// <summary>
        /// The child db context instances attached to this db context instance, declared with
        /// <see cref="Options.DbContextOptions.ParentFor{TDbContext}"/> and resolved from its
        /// same scope: their changes save with <see cref="SaveChangesAsync"/>, and they host
        /// the sources of the cross db context references of this db context. Empty when the
        /// instance is not attached to a scope.
        /// </summary>
        IEnumerable<IDbContext> ChildDbContexts { get; }

        /// <summary>
        /// Internal collection for keep db operations execution log
        /// </summary>
        IRepository<OperationBase, string> DbOperations { get; }

        /// <summary>
        /// List of registered migration tasks
        /// </summary>
        IEnumerable<DocumentMigration> DocumentMigrationList { get; }

        /// <summary>
        /// The scope independent engine serving this db context instance.
        /// </summary>
        IDbContextEngine Engine { get; }

        /// <summary>
        /// True while change tracking is suppressed on this db context instance (see
        /// <see cref="SuppressChangeTracking"/>): the library internals are reading models
        /// to merge or diff loaded data.
        /// </summary>
        bool IsChangeTrackingSuppressed { get; }

        /// <summary>
        /// True if it has been seeded.
        /// </summary>
        bool IsSeeded { get; }

        /// <summary>
        /// Registry of the repositories of this db context instance.
        /// </summary>
        IRepositoryRegistry RepositoryRegistry { get; }

        // Methods.
        /// <summary>
        /// Execute an action into a database transaction, on a new session of this db context
        /// engine. All the operations invoked inside the action on repositories of this db
        /// context enlist automatically in the transaction: it commits when the action
        /// completes, and aborts if it throws, discarding every enlisted operation.
        /// </summary>
        /// <remarks>
        /// Requires a MongoDB deployment supporting transactions (replica set or sharded
        /// cluster). The transaction is scoped to the connection of this db context engine:
        /// operations on different db contexts, children included, don't enlist. Sessions
        /// don't support concurrent operations: keep operations sequential inside the action.
        /// </remarks>
        /// <param name="action">The action to execute into the transaction</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default);

        /// <summary>
        /// Execute a function into a database transaction, on a new session of this db context
        /// engine. All the operations invoked inside the function on repositories of this db
        /// context enlist automatically in the transaction: it commits when the function
        /// completes, and aborts if it throws, discarding every enlisted operation.
        /// </summary>
        /// <remarks>
        /// Requires a MongoDB deployment supporting transactions (replica set or sharded
        /// cluster). The transaction is scoped to the connection of this db context engine:
        /// operations on different db contexts, children included, don't enlist. Sessions
        /// don't support concurrent operations: keep operations sequential inside the function.
        /// </remarks>
        /// <param name="func">The function to execute into the transaction</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The function result</returns>
        Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> func, CancellationToken cancellationToken = default);

        /// <summary>
        /// Execute a db context migration process: delete old indexes, migrate documents, and build new indexes.
        /// The caller must already hold an exclusive access on the db context, except for a dry
        /// run operation, that simulates the document migrations without persisting anything,
        /// skips the index steps, and reports failing documents into the operation logs.
        /// The execution resumes the db context lock claimed with the operation, keeping its
        /// lease renewed and releasing it at completion, unless an outer flow (e.g. seeding)
        /// already holds a lease; an operation whose claim doesn't resume (its lock has been
        /// taken over by another owner, or released) closes cancelled without migrating.
        /// </summary>
        /// <param name="dbMigrationOpId">Id of the migration operation to execute</param>
        /// <param name="taskId">Optional id of the background task running the migration</param>
        /// <param name="throwOnErrors">If true, throw an exception when the migration completes with errors</param>
        Task ExecuteMigrationAsync(string dbMigrationOpId, string? taskId = null, bool throwOnErrors = false);

        Task<List<DbMigrationOperation>> GetLastMigrationsAsync(int page, int take);

        Task<DbMigrationOperation> GetMigrationAsync(string migrateOperationId);

        Task<DbMigrationOperation?> IsMigrationRunningAsync();

        /// <summary>
        /// True if the member is already loaded on the model: always true on a full model,
        /// true on a summary model only for its loaded members. A member not loaded reads
        /// through an implicit lazy load, honoring <see cref="Options.IDbContextOptions.ImplicitLazyLoad"/>.
        /// </summary>
        /// <param name="model">The model</param>
        /// <param name="member">The member to verify, as a direct member access expression</param>
        bool IsMemberLoaded<TModel>(TModel model, Expression<Func<TModel, object?>> member)
            where TModel : class, IEntityModel;

        /// <summary>
        /// True if the model instance is outdated: its document changed type after the
        /// instance materialized, and any application interaction with it throws
        /// <see cref="Exceptions.MongodmOutdatedModelTypeException"/>. Reload the model
        /// from its repository to get the current type.
        /// </summary>
        /// <param name="model">The model to verify</param>
        bool IsOutdatedModel(object model);

        /// <summary>
        /// Ensure that the members are loaded on the model: a no-op when the model is full,
        /// or when a summary model already loaded all of them; otherwise the full document
        /// loads with a single query, merging in place. Members are a precondition, not a
        /// projection: any load is always of the whole document.
        /// </summary>
        /// <param name="model">The model to preload</param>
        /// <param name="members">The members to ensure, as direct member access expressions</param>
        Task LoadValuesAsync<TModel>(TModel model, params Expression<Func<TModel, object?>>[] members)
            where TModel : class, IEntityModel;

        /// <summary>
        /// Ensure that the members are loaded on every model of the collection: the full
        /// documents of the missing ones load grouped per source repository, with one query
        /// per bounded chunk of ids, merging in place. The batch replacement of per
        /// instance lazy loads.
        /// </summary>
        /// <param name="models">The models to preload</param>
        /// <param name="members">The members to ensure, as direct member access expressions</param>
        Task LoadValuesAsync<TModel>(IEnumerable<TModel> models, params Expression<Func<TModel, object?>>[] members)
            where TModel : class, IEntityModel;

        /// <summary>
        /// Remove a model from the change candidates of this db context instance, after its
        /// changes have been saved. Its model document is kept, so following mutations are tracked.
        /// </summary>
        /// <param name="model">The model to clear</param>
        void ClearChangeCandidate(IEntityModel model);

        /// <summary>
        /// Flag a proxy model as a change candidate on this db context instance, invoked by
        /// change tracking on a mutation. The mark is ignored until the model has a model document
        /// (skipping the deserialization sets) and while change tracking is suppressed.
        /// </summary>
        /// <param name="model">The mutated model</param>
        void MarkChangeCandidate(IEntityModel model);

        /// <summary>
        /// React to an implicit lazy load, before it runs, honoring
        /// <see cref="Options.IDbContextOptions.ImplicitLazyLoad"/>: log a warning once per
        /// member per scope, stay silent, or deny the load throwing
        /// <see cref="Exceptions.MongodmLazyLoadingException"/>. Invoked by the proxy models.
        /// </summary>
        /// <param name="modelType">The summary model type</param>
        /// <param name="memberName">The read member, null for an unanalyzed domain method</param>
        void OnImplicitLazyLoad(Type modelType, string? memberName);

        /// <summary>
        /// Register a model instance as the loaded one for its document on this db context
        /// instance. Following loads of the same document will return the same instance.
        /// </summary>
        /// <param name="modelId">The model document id</param>
        /// <param name="model">The loaded model instance</param>
        void RegisterLoadedModel(object modelId, IEntityModel model);

        /// <summary>
        /// Replace the loaded model instance of a document with a fresh one carrying the
        /// current document type, invoked by the load deduplication when a full load finds
        /// the document with another type of its hierarchy. The outdated instance leaves the
        /// change tracking and starts denying any application interaction, throwing
        /// <see cref="Exceptions.MongodmOutdatedModelTypeException"/>.
        /// </summary>
        /// <param name="modelId">The model document id</param>
        /// <param name="outdatedModel">The loaded instance with the outdated type</param>
        /// <param name="currentModel">The fresh instance with the current document type</param>
        void ReplaceOutdatedLoadedModel(object modelId, IEntityModel outdatedModel, IEntityModel currentModel);

        /// <summary>
        /// Remove a model from the change tracking of this db context instance, dropping its
        /// model document and its change candidate flag, keeping it out of the next changes save.
        /// </summary>
        /// <param name="model">The model to remove</param>
        void RemoveModelTracking(IEntityModel model);

        /// <summary>
        /// Save current model changes on db. With <see cref="Options.IDbContextOptions.EnableTransactionsWithReplicaSet"/>
        /// enabled and a deployment supporting transactions, the changed models of this db context
        /// save into a single implicit transaction; when a session is already ambient (e.g. into
        /// <see cref="ExecuteInTransactionAsync(Func{Task}, CancellationToken)"/>), saves enlist
        /// in it instead. Child db contexts save on their own connections, out of both.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        Task SaveChangesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Seed database context if still not seeded, applying a db migration before the seed.
        /// The seeding claims the db context lock, running once per db context across every
        /// application instance connected to the database: while another owner holds the lock
        /// (another application instance, or another flow of this process) the call waits,
        /// re-reading the seeding state from the db.
        /// </summary>
        /// <param name="lockWaitTimeout">Maximum time this seeding waits for the db context
        /// lock held by ANOTHER owner, defaulted to the lease duration of this call. With the
        /// default, the lease of a dead owner always expires inside the wait: only an owner
        /// still alive, and working longer than the wait, fails the seeding. The wait covers
        /// the whole work of the other owner (a seeding, a migration, or a diagnostic dry
        /// run), so it must be generous enough for it: startup seeding blocks on it</param>
        /// <param name="lockLeaseDuration">Duration of the lock lease claimed by THIS seeding,
        /// defaulted to <see cref="Utility.DbContextLock.DefaultLeaseDuration"/>: how long the
        /// db context stays locked if this application instance dies before the seeding
        /// completes. It doesn't have to cover the seeding duration, since the lease is renewed
        /// in background while the seeding runs</param>
        /// <returns>True if seed has been executed. False otherwise</returns>
        /// <exception cref="Exceptions.MongodmDbSeedingException">The seed failed, or the db
        /// context lock stayed held by another owner for the whole wait timeout</exception>
        Task<bool> SeedIfNeededAsync(TimeSpan? lockWaitTimeout = null, TimeSpan? lockLeaseDuration = null);

        /// <summary>
        /// Try to get the model instance already loaded on this db context instance for a
        /// document of a repository.
        /// </summary>
        /// <param name="repository">The repository hosting the document</param>
        /// <param name="modelId">The model document id</param>
        /// <returns>The loaded model instance, or null when absent</returns>
        IEntityModel? TryGetLoadedModel(IRepository repository, object modelId);

        /// <summary>
        /// Try to start a db context migration process, claiming the db context lock with the
        /// new operation as owner: the claim is atomic on the server, so a single start wins
        /// also with concurrent starts from different application instances, and it is denied
        /// while another owner (a queued or running migration, or a seeding) holds the lock.
        /// Operations orphaned by dead owners close at the next start, once their lease expires.
        /// </summary>
        /// <param name="dryRun">If true, start a dry run: simulate the document migrations
        /// without persisting anything, reporting failing documents into the operation logs</param>
        /// <param name="stopAtFirstError">If true, abort a documents migration at its first
        /// failing document, instead of skipping it and processing every other document</param>
        /// <param name="lockLeaseDuration">Duration of the lock lease claimed by this start,
        /// defaulted to <see cref="Utility.DbContextLock.DefaultLeaseDuration"/>: how long the
        /// db context stays locked if this application instance dies before the migration
        /// completes, and how long the claim survives waiting for the background task runner to
        /// pick the operation up, the only window nothing renews it. It doesn't have to cover
        /// the migration duration, since the execution keeps the lease renewed</param>
        /// <returns>The new migration operation, or null when the start is denied: a read-only
        /// db context, an exclusive access already running in this process, or the db context
        /// lock held by another owner</returns>
        Task<DbMigrationOperation?> TryStartMigrationAsync(
            bool dryRun = false,
            bool stopAtFirstError = false,
            TimeSpan? lockLeaseDuration = null);

        /// <summary>
        /// Set the model document of a model on this db context instance: the serialized
        /// form its loaded members are diffed against at save. Captured at load and
        /// create, and refreshed after each save.
        /// </summary>
        /// <param name="model">The tracked model</param>
        /// <param name="bsonDocument">The serialized model document, diffed against at save</param>
        void SetModelBsonDocument(IEntityModel model, BsonDocument bsonDocument);

        /// <summary>
        /// Bind a model to its source repository on this db context instance, for a tracked
        /// model that can't carry it (a created or replaced non proxy instance), so its changes
        /// save to the right repository even when the model type is handled by many repositories.
        /// </summary>
        /// <param name="model">The tracked model</param>
        /// <param name="sourceRepository">The model source repository</param>
        void SetModelSourceRepository(IEntityModel model, IRepository sourceRepository);

        /// <summary>
        /// Start a scope keeping transient the models materialized inside it: at the scope
        /// dispose, every model loaded or tracked on this db context instance after the scope
        /// start evicts from the loaded models and the change tracking, discarding any unsaved
        /// change. Models already loaded or tracked before the scope keep their state, updates
        /// applied inside the scope included. Used by massive scans, like the document
        /// migrations, to keep the scope memory bounded while each element still loads and
        /// saves normally.
        /// </summary>
        /// <returns>The transient models scope</returns>
        IDisposable StartTransientModelsScope();

        /// <summary>
        /// Suppress change tracking on this db context instance until the returned scope is
        /// disposed: mutations don't flag change candidates. Used while merging loaded data
        /// into a model, keeping the merge out of the unit of work.
        /// </summary>
        /// <returns>The suppression scope</returns>
        IDisposable SuppressChangeTracking();

        /// <summary>
        /// Try to get the model document of a model on this db context instance.
        /// </summary>
        /// <param name="model">The tracked model</param>
        /// <returns>The model document, or null when the model is not tracked</returns>
        BsonDocument? TryGetModelBsonDocument(IEntityModel model);

        /// <summary>
        /// Remove a model instance from the loaded models of this db context instance,
        /// keeping it out of next loads deduplication.
        /// </summary>
        /// <param name="modelId">The model document id</param>
        /// <param name="model">The model instance to remove</param>
        void UnregisterLoadedModel(object modelId, IEntityModel model);
    }
}
