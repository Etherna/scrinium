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

using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Migration;
using Etherna.Scrinium.Core.Options;
using Etherna.Scrinium.Core.Repositories;
using Etherna.Scrinium.Core.Utility;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.Scrinium.Core
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
        /// A transient failure — a write conflict with a concurrent transaction, a primary
        /// election — retries the whole block on a new transaction, within
        /// <see cref="Options.IDbContextOptions.TransactionRetryTimeout"/>: the block must be
        /// replayable. The saves enlisted in the transaction refresh their change tracking at
        /// its commit, and the creates undo their in memory effects at its abort (the assigned
        /// ids, the identity map and change tracking registrations): after an abort the saved
        /// models stay pending, saved again by the next flush, and the created models are new
        /// again, created anew by a replay.
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
        /// A transient failure — a write conflict with a concurrent transaction, a primary
        /// election — retries the whole block on a new transaction, within
        /// <see cref="Options.IDbContextOptions.TransactionRetryTimeout"/>: the block must be
        /// replayable. The saves enlisted in the transaction refresh their change tracking at
        /// its commit, and the creates undo their in memory effects at its abort (the assigned
        /// ids, the identity map and change tracking registrations): after an abort the saved
        /// models stay pending, saved again by the next flush, and the created models are new
        /// again, created anew by a replay.
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

        /// <summary>
        /// Execute the missing origin references repair of an operation started before,
        /// closing it completed or failed.
        /// </summary>
        /// <param name="referencesRepairOpId">The operation id</param>
        /// <param name="taskId">The id of the background task executing it</param>
        /// <param name="throwOnErrors">If true, rethrow what failed the operation</param>
        Task ExecuteReferencesRepairAsync(string referencesRepairOpId, string? taskId = null, bool throwOnErrors = false);

        Task<List<DbMigrationOperation>> GetLastMigrationsAsync(int page, int take);

        Task<DbMigrationOperation> GetMigrationAsync(string migrateOperationId);

        /// <summary>
        /// Read a page of the operations of this db context, most recent first, whatever kind
        /// they are: migrations, references repairs and seedings share the operations
        /// collection, and this reads them as they are stored, without filtering by type.
        /// </summary>
        /// <param name="page">The page to read, from 0</param>
        /// <param name="take">The operations of a page</param>
        Task<List<OperationBase>> GetLastOperationsAsync(int page, int take);

        Task<List<ReferencesRepairOperation>> GetLastReferencesRepairsAsync(int page, int take);

        Task<ReferencesRepairOperation> GetReferencesRepairAsync(string referencesRepairOpId);

        Task<DbMigrationOperation?> IsMigrationRunningAsync();

        /// <summary>
        /// The open missing origin references repair operation of this db context, whatever
        /// collection it repairs, or null when none is open. An operation stays open also when
        /// the instance executing it dies: pair it with a live db context lock lease to tell a
        /// repair really in progress from one orphaned by a dead owner.
        /// </summary>
        Task<ReferencesRepairOperation?> IsReferencesRepairRunningAsync();

        /// <summary>
        /// Tell if a live lease, exclusive or shared, holds the lock of an application
        /// resource. A lease whose expiration is passed doesn't lock anything: the next
        /// acquisition takes it over.
        /// </summary>
        /// <param name="resourceNamespace">The namespace of the resource, one per lock kind;
        /// it can't contain the <c>/</c> separator</param>
        /// <param name="resourceId">The resource identifier inside its namespace</param>
        /// <returns>True if the resource lock is held by a live lease</returns>
        Task<bool> IsResourceLockedAsync(string resourceNamespace, string resourceId);

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
        /// <see cref="Exceptions.ScriniumOutdatedModelTypeException"/>. Reload the model
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
        /// Save current model changes on db. With <see cref="Options.IDbContextOptions.EnableTransactionsWithReplicaSet"/>
        /// enabled and a deployment supporting transactions, the changed models of this db context
        /// save into a single implicit transaction; when a session is already ambient (e.g. into
        /// <see cref="ExecuteInTransactionAsync(Func{Task}, CancellationToken)"/>), saves enlist
        /// in it instead. Inside a transaction the saved models refresh and leave the change
        /// candidates only at its commit: an abort leaves them pending, saved again by the next
        /// call, and the implicit transaction retries its transient failures within
        /// <see cref="Options.IDbContextOptions.TransactionRetryTimeout"/>. Child db contexts
        /// save on their own connections, out of both.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        Task SaveChangesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Seed database context if still not seeded, applying a db migration before the seed.
        /// The seeding claims the db context lock, running once per db context across every
        /// application instance connected to the database: while another owner holds the lock
        /// (another application instance, or another flow of this process) the call waits,
        /// re-reading the seeding state from the db. The <see cref="ChildDbContexts"/> seed
        /// first, with the same arguments, so the seed of a parent finds them seeded.
        /// </summary>
        /// <param name="lockWaitTimeout">Maximum time this seeding waits for the db context
        /// lock held by ANOTHER owner, defaulted to the lease duration of this call. With the
        /// default, the lease of a dead owner always expires inside the wait: only an owner
        /// still alive, and working longer than the wait, fails the seeding. The wait covers
        /// the whole work of the other owner (a seeding, a migration, or a diagnostic dry
        /// run), so it must be generous enough for it: startup seeding blocks on it</param>
        /// <param name="lockLeaseDuration">Duration of the lock lease claimed by THIS seeding,
        /// defaulted to <see cref="Utility.ResourceLock.DefaultLeaseDuration"/>: how long the
        /// db context stays locked if this application instance dies before the seeding
        /// completes. It doesn't have to cover the seeding duration, since the lease is renewed
        /// in background while the seeding runs</param>
        /// <returns>True if the seed of this db context has been executed. False otherwise</returns>
        /// <exception cref="Exceptions.ScriniumDbSeedingException">The seed failed, or the db
        /// context lock stayed held by another owner for the whole wait timeout</exception>
        Task<bool> SeedIfNeededAsync(TimeSpan? lockWaitTimeout = null, TimeSpan? lockLeaseDuration = null);

        /// <summary>
        /// Try to acquire the lock of an application resource, atomically on the server,
        /// across every application instance connected to the database. An exclusive
        /// acquisition admits a single holder, denied by any live lease; a shared one admits
        /// any number of holders, each with its own renewed lease, denied only by a live
        /// exclusive lease, and a dead shared holder expires alone, without affecting the
        /// others. An expired lease is taken over. The returned lease keeps renewing in
        /// background, signalling on <see cref="IResourceLockLease.LeaseLostToken"/> when it
        /// can't be assumed alive anymore, until its disposal releases this holder. The
        /// resource id lives in the namespace the application chooses, so locks of different
        /// kinds never collide; a read-only db context denies the lock, since claiming it
        /// would write on a database owned by another application.
        /// </summary>
        /// <param name="resourceNamespace">The namespace of the resource, one per lock kind;
        /// it can't contain the <c>/</c> separator</param>
        /// <param name="resourceId">The resource identifier inside its namespace</param>
        /// <param name="mode">The acquisition mode: a single exclusive holder, or shared
        /// holders coexisting on the resource</param>
        /// <param name="leaseDuration">Duration of the acquired lease, defaulted to
        /// <see cref="ResourceLock.DefaultLeaseDuration"/>: how long this holder locks the
        /// resource if its process dies before releasing it. It doesn't have to cover the
        /// work running under the lease, kept renewed in background</param>
        /// <returns>The active lease, or null when a live lease denies the acquisition</returns>
        Task<IResourceLockLease?> TryAcquireResourceLockAsync(
            string resourceNamespace,
            string resourceId,
            ResourceLockMode mode = ResourceLockMode.Exclusive,
            TimeSpan? leaseDuration = null);

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
        /// defaulted to <see cref="Utility.ResourceLock.DefaultLeaseDuration"/>: how long the
        /// db context stays locked if this application instance dies before the migration
        /// completes, and how long the claim survives waiting for the background task runner to
        /// pick the operation up, the only window nothing renews it. It doesn't have to cover
        /// the migration duration, since the execution keeps the lease renewed</param>
        /// <param name="rewriteDeprecatedSchemas">If true, the migration also rewrites the
        /// documents left on a deprecated schema: the ones whose stored schema id isn't the
        /// active one of their concrete type, the ones carrying it under the deprecated
        /// element name included. They are rewritten whole with their active schema, after
        /// the declared document migrations and inside the same operation</param>
        /// <returns>The new migration operation, or null when the start is denied: a read-only
        /// db context, an exclusive access already running in this process, or the db context
        /// lock held by another owner</returns>
        Task<DbMigrationOperation?> TryStartMigrationAsync(
            bool dryRun = false,
            bool stopAtFirstError = false,
            TimeSpan? lockLeaseDuration = null,
            bool rewriteDeprecatedSchemas = false);

        /// <summary>
        /// Start the repair of the references pointing to missing origin documents on one
        /// collection, claiming the db context lock and enqueuing the execution in background:
        /// it runs like a migration does, reporting its progress one reference element path at
        /// a time.
        /// </summary>
        /// <param name="repositoryName">The repository whose collection is repaired</param>
        /// <param name="repairModesByElementPath">What to apply to each reference element
        /// path: the plan of the operation, readable before it runs</param>
        /// <param name="dryRun">If true, execute the repair with its collection writes
        /// simulated, reporting what it would have done without persisting anything</param>
        /// <param name="lockLeaseDuration">Duration of the lock lease claimed by this start,
        /// defaulted to <see cref="Utility.ResourceLock.DefaultLeaseDuration"/></param>
        /// <returns>The new operation, or null when the start is denied: a read-only db
        /// context, an exclusive access already running in this process, or the db context
        /// lock held by another owner</returns>
        Task<ReferencesRepairOperation?> TryStartReferencesRepairAsync(
            string repositoryName,
            IReadOnlyDictionary<string, OriginDeleteMode> repairModesByElementPath,
            bool dryRun = false,
            TimeSpan? lockLeaseDuration = null);

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
        /// Remove a model instance from the loaded models of this db context instance,
        /// keeping it out of next loads deduplication.
        /// </summary>
        /// <param name="modelId">The model document id</param>
        /// <param name="model">The model instance to remove</param>
        void UnregisterLoadedModel(object modelId, IEntityModel model);
    }
}
