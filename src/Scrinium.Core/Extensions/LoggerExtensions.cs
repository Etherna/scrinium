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

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace Etherna.Scrinium.Core.Extensions
{
    /*
     * Always group similar log delegates by type, always use incremental event ids.
     * Last event id is: 76
     */
    public static class LoggerExtensions
    {
        // Fields.
        //*** TRACE LOGS ***
        private static readonly Action<ILogger, string, string, Exception> _repositoryAccessedCollection =
            LoggerMessage.Define<string, string>(
                LogLevel.Trace,
                new EventId(11, nameof(RepositoryAccessedCollection)),
                "Repository {RepositoryName} of DbContext {DbName} accessed collection");

        //*** DEBUG LOGS ***
        private static readonly Action<ILogger, string, int, int, Exception> _dbContextEvictedTransientModels =
            LoggerMessage.Define<string, int, int>(
                LogLevel.Trace,
                new EventId(59, nameof(DbContextEvictedTransientModels)),
                "DbContext {DbName} evicted at a transient models scope end {LoadedModelsCount} loaded models and {TrackedModelsCount} tracked models");

        private static readonly Action<ILogger, string, int, int, Exception> _dbContextExclusiveAccessDrainingInFlightOperations =
            LoggerMessage.Define<string, int, int>(
                LogLevel.Debug,
                new EventId(76, nameof(DbContextExclusiveAccessDrainingInFlightOperations)),
                "DbContext {DbName} exclusive access is draining the in flight operations: {ReadsCount} reads and {WritesCount} writes still running");

        private static readonly Action<ILogger, string, string, string, Exception> _dbContextRegisteredChangedModel =
            LoggerMessage.Define<string, string, string>(
                LogLevel.Trace,
                new EventId(25, nameof(DbContextRegisteredChangedModel)),
                "DbContext {DbName} registered changed model with Id {ModelId} of repository {RepositoryName}");

        private static readonly Action<ILogger, string, string, string, Exception> _dbContextRegisteredLoadedModel =
            LoggerMessage.Define<string, string, string>(
                LogLevel.Trace,
                new EventId(26, nameof(DbContextRegisteredLoadedModel)),
                "DbContext {DbName} registered loaded model with Id {ModelId} of repository {RepositoryName}");

        private static readonly Action<ILogger, string, string, string, Exception> _dbContextReturnedLoadedModel =
            LoggerMessage.Define<string, string, string>(
                LogLevel.Trace,
                new EventId(27, nameof(DbContextReturnedLoadedModel)),
                "DbContext {DbName} returned already loaded model with Id {ModelId} of repository {RepositoryName}");

        private static readonly Action<ILogger, string, string, string, Exception> _dbContextSavedChangedModelToRepository =
            LoggerMessage.Define<string, string, string>(
                LogLevel.Debug,
                new EventId(3, nameof(DbContextSavedChangedModelToRepository)),
                "DbContext {DbName} saved changed model with Id {ModelId} on repository {RepositoryName}");

        private static readonly Action<ILogger, string, Exception> _dbContextStartedTransaction =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(39, nameof(DbContextStartedTransaction)),
                "DbContext {DbName} started a transaction on a new session");

        private static readonly Action<ILogger, string, string, string, Exception> _dbContextUnregisteredChangedModel =
            LoggerMessage.Define<string, string, string>(
                LogLevel.Trace,
                new EventId(29, nameof(DbContextUnregisteredChangedModel)),
                "DbContext {DbName} unregistered changed model with Id {ModelId} of repository {RepositoryName}");

        private static readonly Action<ILogger, string, string, string, Exception> _dbContextUnregisteredLoadedModel =
            LoggerMessage.Define<string, string, string>(
                LogLevel.Trace,
                new EventId(30, nameof(DbContextUnregisteredLoadedModel)),
                "DbContext {DbName} unregistered loaded model with Id {ModelId} of repository {RepositoryName}");

        private static readonly Action<ILogger, string, Type, string, int, Exception> _dbMaintainerEnqueuedDependenciesDeleteTask =
            LoggerMessage.Define<string, Type, string, int>(
                LogLevel.Debug,
                new EventId(66, nameof(DbMaintainerEnqueuedDependenciesDeleteTask)),
                "DbContext {DbName} enqueued dependencies delete task for deleted model type {ModelType} with Id {ModelId}, involving {IdMemberMapsCount} id member maps");

        private static readonly Action<ILogger, string, Type, string, int, Exception> _dbMaintainerEnqueuedDependenciesUpdateTask =
            LoggerMessage.Define<string, Type, string, int>(
                LogLevel.Trace,
                new EventId(31, nameof(DbMaintainerEnqueuedDependenciesUpdateTask)),
                "DbMaintainer of DbContext {DbName} enqueued dependencies update task for model {ModelType} with Id {ModelId} on {IdMemberMapsCount} id member maps");

        private static readonly Action<ILogger, string, string, Type, string, int, Exception> _dbMaintainerEnqueuedParentDependenciesDeleteTask =
            LoggerMessage.Define<string, string, Type, string, int>(
                LogLevel.Debug,
                new EventId(73, nameof(DbMaintainerEnqueuedParentDependenciesDeleteTask)),
                "DbContext {DbName} enqueued dependencies delete task on parent DbContext {ParentDbName} for deleted model type {ModelType} with Id {ModelId}, involving {IdMemberMapsCount} id member maps");

        private static readonly Action<ILogger, string, string, Type, string, int, Exception> _dbMaintainerEnqueuedParentDependenciesUpdateTask =
            LoggerMessage.Define<string, string, Type, string, int>(
                LogLevel.Trace,
                new EventId(74, nameof(DbMaintainerEnqueuedParentDependenciesUpdateTask)),
                "DbMaintainer of DbContext {DbName} enqueued dependencies update task on parent DbContext {ParentDbName} for model {ModelType} with Id {ModelId} on {IdMemberMapsCount} id member maps");

        private static readonly Action<ILogger, string, Exception> _dbMaintainerInitialized =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(8, nameof(DbMaintainerInitialized)),
                "DbMaintainer of DbContext {DbName} initialized");

        private static readonly Action<ILogger, string, string, Exception> _dbMaintainerSkippedDependenciesDeleteOnDryRun =
            LoggerMessage.Define<string, string>(
                LogLevel.Debug,
                new EventId(67, nameof(DbMaintainerSkippedDependenciesDeleteOnDryRun)),
                "DbContext {DbName} skipped dependencies delete of deleted model with Id {ModelId} on dry run");

        private static readonly Action<ILogger, string, string, Exception> _dbMaintainerSkippedDependenciesDeleteWithoutPolicies =
            LoggerMessage.Define<string, string>(
                LogLevel.Debug,
                new EventId(68, nameof(DbMaintainerSkippedDependenciesDeleteWithoutPolicies)),
                "DbContext {DbName} skipped dependencies delete of deleted model with Id {ModelId}: no reference declares an origin delete policy on it");

        private static readonly Action<ILogger, string, string, Exception> _dbMaintainerSkippedDependenciesUpdateOnDryRun =
            LoggerMessage.Define<string, string>(
                LogLevel.Debug,
                new EventId(48, nameof(DbMaintainerSkippedDependenciesUpdateOnDryRun)),
                "DbMaintainer of DbContext {DbName} skipped dependencies update task for model with Id {ModelId} on a dry run");

        private static readonly Action<ILogger, string, string, Exception> _dbMaintainerSkippedDependenciesUpdateWithoutReferences =
            LoggerMessage.Define<string, string>(
                LogLevel.Trace,
                new EventId(50, nameof(DbMaintainerSkippedDependenciesUpdateWithoutReferences)),
                "DbMaintainer of DbContext {DbName} skipped dependencies update task for model with Id {ModelId} without involved reference members");

        private static readonly Action<ILogger, string, Exception> _dbMigrationManagerInitialized =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(9, nameof(DbMigrationManagerInitialized)),
                "DbMigrationManager of DbContext {DbName} initialized");

        private static readonly Action<ILogger, Type, string, string, Exception> _deleteDocDependenciesTaskEnded =
            LoggerMessage.Define<Type, string, string>(
                LogLevel.Debug,
                new EventId(70, nameof(DeleteDocDependenciesTaskEnded)),
                "DeleteDocDependenciesTask ended on DbContext {DbContextType} with deleted repository {DeletedRepositoryName} to model Id {ModelId}");

        private static readonly Action<ILogger, Type, string, string, IEnumerable<string>, Exception> _deleteDocDependenciesTaskStarted =
            LoggerMessage.Define<Type, string, string, IEnumerable<string>>(
                LogLevel.Debug,
                new EventId(69, nameof(DeleteDocDependenciesTaskStarted)),
                "DeleteDocDependenciesTask started on DbContext {DbContextType} with deleted repository {DeletedRepositoryName}, propagating deleted model Id {ModelId} on Id's member maps: {IdMemberMapIdentifiers}");

        private static readonly Action<ILogger, string, Exception> _discriminatorRegistryInitialized =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(4, nameof(DiscriminatorRegistryInitialized)),
                "DiscriminatorRegistry of DbContext {DbName} initialized");

        private static readonly Action<ILogger, string, string, string, Exception> _repositoryCreatedDocument =
            LoggerMessage.Define<string, string, string>(
                LogLevel.Debug,
                new EventId(14, nameof(RepositoryCreatedDocument)),
                "Repository {RepositoryName} of DbContext {DbName} created document with Id: {ModelId}");

        private static readonly Action<ILogger, string, string, IEnumerable<string>, Exception> _repositoryCreatedDocuments =
            LoggerMessage.Define<string, string, IEnumerable<string>>(
                LogLevel.Debug,
                new EventId(13, nameof(RepositoryCreatedDocuments)),
                "Repository {RepositoryName} of DbContext {DbName} created multiple documents with Ids: {ModelsId}");

        private static readonly Action<ILogger, string, string, string, Exception> _repositoryDeletedDocument =
            LoggerMessage.Define<string, string, string>(
                LogLevel.Debug,
                new EventId(15, nameof(RepositoryDeletedDocument)),
                "Repository {RepositoryName} of DbContext {DbName} deleted document with Id: {ModelId}");

        private static readonly Action<ILogger, string, string, string, Exception> _repositoryFoundDocument =
            LoggerMessage.Define<string, string, string>(
                LogLevel.Debug,
                new EventId(16, nameof(RepositoryFoundDocument)),
                "Repository {RepositoryName} of DbContext {DbName} found document with Id: {ModelId}");

        private static readonly Action<ILogger, string, string, Exception> _repositoryInitialized =
            LoggerMessage.Define<string, string>(
                LogLevel.Debug,
                new EventId(10, nameof(RepositoryInitialized)),
                "Repository {RepositoryName} of DbContext {DbName} initialized");

        private static readonly Action<ILogger, string, Exception> _repositoryRegistryInitialized =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(5, nameof(RepositoryRegistryInitialized)),
                "RepositoryRegistry of DbContext {DbName} initialized");

        private static readonly Action<ILogger, string, string, string, string, Exception> _repositoryRemovedMissingOriginReference =
            LoggerMessage.Define<string, string, string, string>(
                LogLevel.Debug,
                new EventId(64, nameof(RepositoryRemovedMissingOriginReference)),
                "Repository {RepositoryName} of DbContext {DbName} removed the references to missing origin document {MissingOriginId} at path {ElementPath}");

        private static readonly Action<ILogger, string, string, string, Exception> _repositoryReplacedDocument =
            LoggerMessage.Define<string, string, string>(
                LogLevel.Debug,
                new EventId(17, nameof(RepositoryReplacedDocument)),
                "Repository {RepositoryName} of DbContext {DbName} replaced document with Id: {ModelId}");

        private static readonly Action<ILogger, string, string, string, Exception> _repositorySavedModelChanges =
            LoggerMessage.Define<string, string, string>(
                LogLevel.Debug,
                new EventId(23, nameof(RepositorySavedModelChanges)),
                "Repository {RepositoryName} of DbContext {DbName} saved changed members of document with Id: {ModelId}");

        private static readonly Action<ILogger, string, string, string, string, Exception> _repositorySaveFellBackToDocumentReplace =
            LoggerMessage.Define<string, string, string, string>(
                LogLevel.Trace,
                new EventId(34, nameof(RepositorySaveFellBackToDocumentReplace)),
                "Repository {RepositoryName} of DbContext {DbName} saving changes of document with Id: {ModelId} fell back to document replace: {Reason}");

        private static readonly Action<ILogger, string, string, string, Exception> _repositorySkippedDependenciesUpdate =
            LoggerMessage.Define<string, string, string>(
                LogLevel.Trace,
                new EventId(35, nameof(RepositorySkippedDependenciesUpdate)),
                "Repository {RepositoryName} of DbContext {DbName} skipped dependencies update of not found document with Id: {ModelId}");

        private static readonly Action<ILogger, string, string, bool, Exception> _repositoryUpsertedDocument =
            LoggerMessage.Define<string, string, bool>(
                LogLevel.Trace,
                new EventId(36, nameof(RepositoryUpsertedDocument)),
                "Repository {RepositoryName} of DbContext {DbName} upserted document, inserted: {Inserted}");

        private static readonly Action<ILogger, Type, string, string, Exception> _updateDocDependenciesTaskEnded =
            LoggerMessage.Define<Type, string, string>(
                LogLevel.Debug,
                new EventId(20, nameof(UpdateDocDependenciesTaskEnded)),
                "UpdateDocDependenciesTask ended on DbContext {DbContextType} with reference repository {ReferenceRepositoryName} to model Id {ModelId}");

        private static readonly Action<ILogger, Type, string, string, IEnumerable<string>, Exception> _updateDocDependenciesTaskStarted =
            LoggerMessage.Define<Type, string, string, IEnumerable<string>>(
                LogLevel.Debug,
                new EventId(19, nameof(UpdateDocDependenciesTaskStarted)),
                "UpdateDocDependenciesTask started on DbContext {DbContextType} with reference repository {ReferenceRepositoryName}, searching for model Id {ModelId} on Id's member maps: {IdMemberMapIdentifiers}");

        private static readonly Action<ILogger, string, Exception> _schemaRegistryInitialized =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(6, nameof(SchemaRegistryInitialized)),
                "SchemaRegistry of DbContext {DbName} initialized");

        //*** INFORMATION LOGS ***
        private static readonly Action<ILogger, string, Exception> _dbContextAttachedToEngine =
            LoggerMessage.Define<string>(
                LogLevel.Trace,
                new EventId(24, nameof(DbContextAttachedToEngine)),
                "DbContext {DbName} attached a new instance to its engine");

        private static readonly Action<ILogger, string, Exception> _dbContextCommittedTransaction =
            LoggerMessage.Define<string>(
                LogLevel.Information,
                new EventId(40, nameof(DbContextCommittedTransaction)),
                "DbContext {DbName} committed transaction");

        private static readonly Action<ILogger, string, Exception> _dbContextInitialized =
            LoggerMessage.Define<string>(
                LogLevel.Information,
                new EventId(0, nameof(DbContextInitialized)),
                "DbContext {DbName} initialized");

        private static readonly Action<ILogger, string, Exception> _dbContextSavedChanges =
            LoggerMessage.Define<string>(
                LogLevel.Information,
                new EventId(1, nameof(DbContextSavedChanges)),
                "DbContext {DbName} saved changes");

        private static readonly Action<ILogger, string, int, Exception> _dbContextSavingChanges =
            LoggerMessage.Define<string, int>(
                LogLevel.Trace,
                new EventId(28, nameof(DbContextSavingChanges)),
                "DbContext {DbName} saving changes of {ChangedModelsCount} models");

        private static readonly Action<ILogger, string, Exception> _dbContextSeeded =
            LoggerMessage.Define<string>(
                LogLevel.Information,
                new EventId(2, nameof(DbContextSeeded)),
                "DbContext {DbName} has been seeded");

        private static readonly Action<ILogger, string, Exception> _dbContextSeedingSkippedOnReadOnly =
            LoggerMessage.Define<string>(
                LogLevel.Information,
                new EventId(46, nameof(DbContextSeedingSkippedOnReadOnly)),
                "DbContext {DbName} skipped seeding: the db context is read-only");

        private static readonly Action<ILogger, string, Exception> _dbContextSeedingWaitingForLock =
            LoggerMessage.Define<string>(
                LogLevel.Information,
                new EventId(56, nameof(DbContextSeedingWaitingForLock)),
                "DbContext {DbName} is waiting for the db context lock before seeding: another instance may be seeding or migrating");

        private static readonly Action<ILogger, string, string, int, Exception> _repositoryAutoCreatedNewReferredModels =
            LoggerMessage.Define<string, string, int>(
                LogLevel.Information,
                new EventId(47, nameof(RepositoryAutoCreatedNewReferredModels)),
                "Repository {RepositoryName} of DbContext {DbName} auto created {NewModelsCount} new referred models");

        private static readonly Action<ILogger, string, string, Exception> _repositoryBuiltIndexes =
            LoggerMessage.Define<string, string>(
                LogLevel.Information,
                new EventId(12, nameof(RepositoryBuiltIndexes)),
                "Repository {RepositoryName} of DbContext {DbName} built indexes");

        private static readonly Action<ILogger, string, string, int, Exception> _repositoryCountedDocumentsBySchemaId =
            LoggerMessage.Define<string, string, int>(
                LogLevel.Information,
                new EventId(49, nameof(RepositoryCountedDocumentsBySchemaId)),
                "Repository {RepositoryName} of DbContext {DbName} counted documents by schema id, found {SchemaIdsCount} schema ids");

        private static readonly Action<ILogger, string, string, long, Exception> _repositoryCountedDeprecatedSchemaIdDocuments =
            LoggerMessage.Define<string, string, long>(
                LogLevel.Information,
                new EventId(77, nameof(RepositoryCountedDeprecatedSchemaIdDocuments)),
                "Repository {RepositoryName} of DbContext {DbName} counted {DocumentsCount} documents carrying the schema id with a deprecated element name");

        private static readonly Action<ILogger, string, string, long, Exception> _repositoryDeletedDocuments =
            LoggerMessage.Define<string, string, long>(
                LogLevel.Information,
                new EventId(22, nameof(RepositoryDeletedDocuments)),
                "Repository {RepositoryName} of DbContext {DbName} deleted {DeletedCount} documents with filter");

        private static readonly Action<ILogger, string, string, bool, Exception> _repositoryFoundAndUpdatedDocument =
            LoggerMessage.Define<string, string, bool>(
                LogLevel.Trace,
                new EventId(33, nameof(RepositoryFoundAndUpdatedDocument)),
                "Repository {RepositoryName} of DbContext {DbName} executed find and update on a document, matched: {Matched}");

        private static readonly Action<ILogger, string, string, long, Exception> _repositoryFoundMissingOriginReferences =
            LoggerMessage.Define<string, string, long>(
                LogLevel.Information,
                new EventId(63, nameof(RepositoryFoundMissingOriginReferences)),
                "Repository {RepositoryName} of DbContext {DbName} found {MissingOriginIdsCount} referenced ids with missing origin document");

        private static readonly Action<ILogger, string, string, long, long, Exception> _repositoryMigratedDeprecatedSchemaIdDocuments =
            LoggerMessage.Define<string, string, long, long>(
                LogLevel.Information,
                new EventId(78, nameof(RepositoryMigratedDeprecatedSchemaIdDocuments)),
                "Repository {RepositoryName} of DbContext {DbName} migrated {MigratedDocumentsCount} documents carrying the schema id with a deprecated element name, {DocumentErrorsCount} failing");

        private static readonly Action<ILogger, string, string, Exception> _repositoryQueriedCollection =
            LoggerMessage.Define<string, string>(
                LogLevel.Information,
                new EventId(18, nameof(RepositoryQueriedCollection)),
                "Repository {RepositoryName} of DbContext {DbName} queried collection");

        private static readonly Action<ILogger, string, string, long, long, Exception> _repositoryRemovedMissingOriginReferences =
            LoggerMessage.Define<string, string, long, long>(
                LogLevel.Information,
                new EventId(65, nameof(RepositoryRemovedMissingOriginReferences)),
                "Repository {RepositoryName} of DbContext {DbName} removed the references to {MissingOriginIdsCount} missing origin documents, updating {UpdatedDocumentsCount} documents");

        //*** WARNING LOGS ***
        private static readonly Action<ILogger, string, string, string?, Exception> _dbContextImplicitLazyLoad =
            LoggerMessage.Define<string, string, string?>(
                LogLevel.Warning,
                new EventId(43, nameof(DbContextImplicitLazyLoad)),
                "DbContext {DbName} implicitly lazy loaded model type {ModelType} reading member {MemberName}: prefer an explicit preload with LoadValuesAsync");

        private static readonly Action<ILogger, string, string, string, Exception> _dbContextMissingOriginDocument =
            LoggerMessage.Define<string, string, string>(
                LogLevel.Warning,
                new EventId(62, nameof(DbContextMissingOriginDocument)),
                "DbContext {DbName} found no origin document loading a summary model of type {ModelType} from repository {RepositoryName}: the referred document doesn't exist on its collection");

        private static readonly Action<ILogger, string, string, Exception> _dbMigrationCancelledWithoutLockClaim =
            LoggerMessage.Define<string, string>(
                LogLevel.Warning,
                new EventId(54, nameof(DbMigrationCancelledWithoutLockClaim)),
                "Db migration operation {DbMigrationOpId} of DbContext {DbName} cancelled: the operation doesn't own the db context lock anymore");

        private static readonly Action<ILogger, long, string, Exception> _dbMigrationClosedOrphanedOperations =
            LoggerMessage.Define<long, string>(
                LogLevel.Warning,
                new EventId(55, nameof(DbMigrationClosedOrphanedOperations)),
                "DbMigrationManager closed {OperationsCount} migration operations of DbContext {DbName}, orphaned by dead owners with expired lock leases");

        private static readonly Action<ILogger, string, string, Exception> _dbMigrationDeniedStartCleanupFailed =
            LoggerMessage.Define<string, string>(
                LogLevel.Warning,
                new EventId(58, nameof(DbMigrationDeniedStartCleanupFailed)),
                "Db migration operation {DbMigrationOpId} of DbContext {DbName} didn't claim the db context lock, and couldn't be deleted: it closes with the orphaned operations at the next start");

        private static readonly Action<ILogger, string, string, Exception> _dbMigrationStartCleanupFailed =
            LoggerMessage.Define<string, string>(
                LogLevel.Warning,
                new EventId(57, nameof(DbMigrationStartCleanupFailed)),
                "Db migration operation {DbMigrationOpId} of DbContext {DbName} failed to start, and couldn't release the db context lock it claimed: the lease will expire on its own");

        private static readonly Action<ILogger, string, Exception> _dbContextAbortedTransaction =
            LoggerMessage.Define<string>(
                LogLevel.Warning,
                new EventId(41, nameof(DbContextAbortedTransaction)),
                "DbContext {DbName} aborted transaction");

        private static readonly Action<ILogger, string, string, string, string, Exception> _dbContextReplacedOutdatedLoadedModel =
            LoggerMessage.Define<string, string, string, string>(
                LogLevel.Warning,
                new EventId(44, nameof(DbContextReplacedOutdatedLoadedModel)),
                "DbContext {DbName} replaced outdated loaded model with Id {ModelId}: its document changed type from {OutdatedModelType} to {CurrentModelType}");

        private static readonly Action<ILogger, string, Exception> _lockCollectionTtlIndexCreationFailed =
            LoggerMessage.Define<string>(
                LogLevel.Warning,
                new EventId(75, nameof(LockCollectionTtlIndexCreationFailed)),
                "DbContext {DbName} couldn't create the TTL index of its lock collection: the abandoned lock documents won't be garbage collected");

        private static readonly Action<ILogger, string, string, Type, Exception> _mapRegistryFoundNotPropagatedReferencePath =
            LoggerMessage.Define<string, string, Type>(
                LogLevel.Warning,
                new EventId(71, nameof(MapRegistryFoundNotPropagatedReferencePath)),
                "DbContext {DbName} maps references behind an unknown document key, at element path {ElementPath} of model type {ModelType}: the dependencies update propagation can't address the path, so its summaries go stale when the referenced models change, the origin delete propagation leaves its references untouched, and the missing origin references scan can't verify them");

        private static readonly Action<ILogger, string, string, string?, Exception> _modelMapSerializerUnrecognizedSchemaId =
            LoggerMessage.Define<string, string, string?>(
                LogLevel.Warning,
                new EventId(60, nameof(ModelMapSerializerUnrecognizedSchemaId)),
                "ModelMapSerializer of DbContext {DbName} deserialized a document of model type {ModelType} with the active schema: its model map schema id {SchemaId} is not recognized, and no fallback is configured");

        private static readonly Action<ILogger, string, string, string?, Exception> _referenceSerializerUnrecognizedSchemaId =
            LoggerMessage.Define<string, string, string?>(
                LogLevel.Warning,
                new EventId(61, nameof(ReferenceSerializerUnrecognizedSchemaId)),
                "ReferenceSerializer of DbContext {DbName} deserialized a reference document of model type {ModelType} reading only its id: its model map schema id {SchemaId} is not recognized, and no fallback is configured, so every other member lazy loads from the origin document");

        private static readonly Action<ILogger, string, string, Exception> _resourceLockLeaseRenewalFailed =
            LoggerMessage.Define<string, string>(
                LogLevel.Warning,
                new EventId(52, nameof(ResourceLockLeaseRenewalFailed)),
                "Resource lock {LockId} lease renewal failed for owner {OwnerId}: retrying until the lease expiration");

        private static readonly Action<ILogger, string, string, Exception> _resourceLockReleaseFailed =
            LoggerMessage.Define<string, string>(
                LogLevel.Warning,
                new EventId(53, nameof(ResourceLockReleaseFailed)),
                "Resource lock {LockId} release failed for owner {OwnerId}: the lease will expire on its own");

        private static readonly Action<ILogger, Type, string, string, Exception> _updateDocDependenciesTaskSkippedOnDeletedModel =
            LoggerMessage.Define<Type, string, string>(
                LogLevel.Warning,
                new EventId(45, nameof(UpdateDocDependenciesTaskSkippedOnDeletedModel)),
                "UpdateDocDependenciesTask skipped on DbContext {DbContextType} with reference repository {ReferenceRepositoryName}: model Id {ModelId} doesn't exist anymore");

        private static readonly Action<ILogger, Type, string, Exception> _updateDocDependenciesTaskSkippedOnUnknownRepository =
            LoggerMessage.Define<Type, string>(
                LogLevel.Warning,
                new EventId(72, nameof(UpdateDocDependenciesTaskSkippedOnUnknownRepository)),
                "UpdateDocDependenciesTask skipped on DbContext {DbContextType}: reference repository {ReferenceRepositoryName} doesn't exist in the current configuration");

        //*** ERROR LOGS ***
        private static readonly Action<ILogger, string, string, Exception> _dbMigrationFailed =
            LoggerMessage.Define<string, string>(
                LogLevel.Error,
                new EventId(42, nameof(DbMigrationFailed)),
                "Db migration operation {DbMigrationOpId} of DbContext {DbName} failed");

        private static readonly Action<ILogger, string, string, Exception> _resourceLockLeaseLost =
            LoggerMessage.Define<string, string>(
                LogLevel.Error,
                new EventId(51, nameof(ResourceLockLeaseLost)),
                "Resource lock {LockId} lease lost by owner {OwnerId}: another claimer may already hold the lock");

        //*** FATAL LOGS ***

        // Methods.
        public static void DbContextAbortedTransaction(this ILogger logger, string dbName) =>
            _dbContextAbortedTransaction(logger, dbName, null!);

        public static void DbContextAttachedToEngine(this ILogger logger, string dbName) =>
            _dbContextAttachedToEngine(logger, dbName, null!);

        public static void DbContextCommittedTransaction(this ILogger logger, string dbName) =>
            _dbContextCommittedTransaction(logger, dbName, null!);

        public static void DbContextEvictedTransientModels(this ILogger logger, string dbName, int loadedModelsCount, int trackedModelsCount) =>
            _dbContextEvictedTransientModels(logger, dbName, loadedModelsCount, trackedModelsCount, null!);

        public static void DbContextExclusiveAccessDrainingInFlightOperations(this ILogger logger, string dbName, int readsCount, int writesCount) =>
            _dbContextExclusiveAccessDrainingInFlightOperations(logger, dbName, readsCount, writesCount, null!);

        public static void DbContextImplicitLazyLoad(this ILogger logger, string dbName, string modelType, string? memberName) =>
            _dbContextImplicitLazyLoad(logger, dbName, modelType, memberName, null!);

        public static void DbContextInitialized(this ILogger logger, string dbName) =>
            _dbContextInitialized(logger, dbName, null!);

        public static void DbContextMissingOriginDocument(this ILogger logger, string dbName, string modelType, string repositoryName) =>
            _dbContextMissingOriginDocument(logger, dbName, modelType, repositoryName, null!);

        public static void DbContextRegisteredChangedModel(this ILogger logger, string dbName, string modelId, string repositoryName) =>
            _dbContextRegisteredChangedModel(logger, dbName, modelId, repositoryName, null!);

        public static void DbContextRegisteredLoadedModel(this ILogger logger, string dbName, string modelId, string repositoryName) =>
            _dbContextRegisteredLoadedModel(logger, dbName, modelId, repositoryName, null!);

        public static void DbContextReplacedOutdatedLoadedModel(this ILogger logger, string dbName, string modelId, string outdatedModelType, string currentModelType) =>
            _dbContextReplacedOutdatedLoadedModel(logger, dbName, modelId, outdatedModelType, currentModelType, null!);

        public static void DbContextReturnedLoadedModel(this ILogger logger, string dbName, string modelId, string repositoryName) =>
            _dbContextReturnedLoadedModel(logger, dbName, modelId, repositoryName, null!);

        public static void DbContextSavedChangedModelToRepository(this ILogger logger, string dbName, string modelId, string repositoryName) =>
            _dbContextSavedChangedModelToRepository(logger, dbName, modelId, repositoryName, null!);

        public static void DbContextSavedChanges(this ILogger logger, string dbName) =>
            _dbContextSavedChanges(logger, dbName, null!);

        public static void DbContextSavingChanges(this ILogger logger, string dbName, int changedModelsCount) =>
            _dbContextSavingChanges(logger, dbName, changedModelsCount, null!);

        public static void DbContextSeeded(this ILogger logger, string dbName) =>
            _dbContextSeeded(logger, dbName, null!);

        public static void DbContextSeedingSkippedOnReadOnly(this ILogger logger, string dbName) =>
            _dbContextSeedingSkippedOnReadOnly(logger, dbName, null!);

        public static void DbContextSeedingWaitingForLock(this ILogger logger, string dbName) =>
            _dbContextSeedingWaitingForLock(logger, dbName, null!);

        public static void DbContextStartedTransaction(this ILogger logger, string dbName) =>
            _dbContextStartedTransaction(logger, dbName, null!);

        public static void DbContextUnregisteredChangedModel(this ILogger logger, string dbName, string modelId, string repositoryName) =>
            _dbContextUnregisteredChangedModel(logger, dbName, modelId, repositoryName, null!);

        public static void DbContextUnregisteredLoadedModel(this ILogger logger, string dbName, string modelId, string repositoryName) =>
            _dbContextUnregisteredLoadedModel(logger, dbName, modelId, repositoryName, null!);

        public static void DbMaintainerEnqueuedDependenciesDeleteTask(this ILogger logger, string dbName, Type modelType, string modelId, int idMemberMapsCount) =>
            _dbMaintainerEnqueuedDependenciesDeleteTask(logger, dbName, modelType, modelId, idMemberMapsCount, null!);

        public static void DbMaintainerEnqueuedDependenciesUpdateTask(this ILogger logger, string dbName, Type modelType, string modelId, int idMemberMapsCount) =>
            _dbMaintainerEnqueuedDependenciesUpdateTask(logger, dbName, modelType, modelId, idMemberMapsCount, null!);

        public static void DbMaintainerEnqueuedParentDependenciesDeleteTask(this ILogger logger, string dbName, string parentDbName, Type modelType, string modelId, int idMemberMapsCount) =>
            _dbMaintainerEnqueuedParentDependenciesDeleteTask(logger, dbName, parentDbName, modelType, modelId, idMemberMapsCount, null!);

        public static void DbMaintainerEnqueuedParentDependenciesUpdateTask(this ILogger logger, string dbName, string parentDbName, Type modelType, string modelId, int idMemberMapsCount) =>
            _dbMaintainerEnqueuedParentDependenciesUpdateTask(logger, dbName, parentDbName, modelType, modelId, idMemberMapsCount, null!);

        public static void DbMaintainerInitialized(this ILogger logger, string dbName) =>
            _dbMaintainerInitialized(logger, dbName, null!);

        public static void DbMaintainerSkippedDependenciesDeleteOnDryRun(this ILogger logger, string dbName, string modelId) =>
            _dbMaintainerSkippedDependenciesDeleteOnDryRun(logger, dbName, modelId, null!);

        public static void DbMaintainerSkippedDependenciesDeleteWithoutPolicies(this ILogger logger, string dbName, string modelId) =>
            _dbMaintainerSkippedDependenciesDeleteWithoutPolicies(logger, dbName, modelId, null!);

        public static void DbMaintainerSkippedDependenciesUpdateOnDryRun(this ILogger logger, string dbName, string modelId) =>
            _dbMaintainerSkippedDependenciesUpdateOnDryRun(logger, dbName, modelId, null!);

        public static void DbMaintainerSkippedDependenciesUpdateWithoutReferences(this ILogger logger, string dbName, string modelId) =>
            _dbMaintainerSkippedDependenciesUpdateWithoutReferences(logger, dbName, modelId, null!);

        public static void DbMigrationCancelledWithoutLockClaim(this ILogger logger, string dbMigrationOpId, string dbName) =>
            _dbMigrationCancelledWithoutLockClaim(logger, dbMigrationOpId, dbName, null!);

        public static void DbMigrationClosedOrphanedOperations(this ILogger logger, long operationsCount, string dbName) =>
            _dbMigrationClosedOrphanedOperations(logger, operationsCount, dbName, null!);

        public static void DbMigrationFailed(this ILogger logger, string dbMigrationOpId, string dbName, Exception exception) =>
            _dbMigrationFailed(logger, dbMigrationOpId, dbName, exception);

        public static void DbMigrationManagerInitialized(this ILogger logger, string dbName) =>
            _dbMigrationManagerInitialized(logger, dbName, null!);

        public static void DbMigrationDeniedStartCleanupFailed(this ILogger logger, string dbMigrationOpId, string dbName, Exception exception) =>
            _dbMigrationDeniedStartCleanupFailed(logger, dbMigrationOpId, dbName, exception);

        public static void DbMigrationStartCleanupFailed(this ILogger logger, string dbMigrationOpId, string dbName, Exception exception) =>
            _dbMigrationStartCleanupFailed(logger, dbMigrationOpId, dbName, exception);

        public static void DeleteDocDependenciesTaskEnded(this ILogger logger, Type dbContextType, string deletedRepositoryName, string modelId) =>
            _deleteDocDependenciesTaskEnded(logger, dbContextType, deletedRepositoryName, modelId, null!);

        public static void DeleteDocDependenciesTaskStarted(this ILogger logger, Type dbContextType, string deletedRepositoryName, string modelId, IEnumerable<string> idMemberMapIdentifiers) =>
            _deleteDocDependenciesTaskStarted(logger, dbContextType, deletedRepositoryName, modelId, idMemberMapIdentifiers, null!);

        public static void DiscriminatorRegistryInitialized(this ILogger logger, string dbName) =>
            _discriminatorRegistryInitialized(logger, dbName, null!);

        public static void LockCollectionTtlIndexCreationFailed(this ILogger logger, string dbName, Exception exception) =>
            _lockCollectionTtlIndexCreationFailed(logger, dbName, exception);

        public static void MapRegistryFoundNotPropagatedReferencePath(this ILogger logger, string dbName, string elementPath, Type modelType) =>
            _mapRegistryFoundNotPropagatedReferencePath(logger, dbName, elementPath, modelType, null!);

        public static void ModelMapSerializerUnrecognizedSchemaId(this ILogger logger, string dbName, string modelType, string? schemaId) =>
            _modelMapSerializerUnrecognizedSchemaId(logger, dbName, modelType, schemaId, null!);

        public static void ReferenceSerializerUnrecognizedSchemaId(this ILogger logger, string dbName, string modelType, string? schemaId) =>
            _referenceSerializerUnrecognizedSchemaId(logger, dbName, modelType, schemaId, null!);

        public static void RepositoryAccessedCollection(this ILogger logger, string repositoryName, string dbName) =>
            _repositoryAccessedCollection(logger, repositoryName, dbName, null!);

        public static void RepositoryAutoCreatedNewReferredModels(this ILogger logger, string repositoryName, string dbName, int newModelsCount) =>
            _repositoryAutoCreatedNewReferredModels(logger, repositoryName, dbName, newModelsCount, null!);

        public static void RepositoryBuiltIndexes(this ILogger logger, string repositoryName, string dbName) =>
            _repositoryBuiltIndexes(logger, repositoryName, dbName, null!);

        public static void RepositoryCountedDocumentsBySchemaId(this ILogger logger, string repositoryName, string dbName, int schemaIdsCount) =>
            _repositoryCountedDocumentsBySchemaId(logger, repositoryName, dbName, schemaIdsCount, null!);

        public static void RepositoryCountedDeprecatedSchemaIdDocuments(this ILogger logger, string repositoryName, string dbName, long documentsCount) =>
            _repositoryCountedDeprecatedSchemaIdDocuments(logger, repositoryName, dbName, documentsCount, null!);

        public static void RepositoryCreatedDocument(this ILogger logger, string repositoryName, string dbName, string modelId) =>
            _repositoryCreatedDocument(logger, repositoryName, dbName, modelId, null!);

        public static void RepositoryCreatedDocuments(this ILogger logger, string repositoryName, string dbName, IEnumerable<string> modelsId) =>
            _repositoryCreatedDocuments(logger, repositoryName, dbName, modelsId, null!);

        public static void RepositoryDeletedDocument(this ILogger logger, string repositoryName, string dbName, string modelId) =>
            _repositoryDeletedDocument(logger, repositoryName, dbName, modelId, null!);

        public static void RepositoryDeletedDocuments(this ILogger logger, string repositoryName, string dbName, long deletedCount) =>
            _repositoryDeletedDocuments(logger, repositoryName, dbName, deletedCount, null!);

        public static void RepositoryFoundAndUpdatedDocument(this ILogger logger, string repositoryName, string dbName, bool matched) =>
            _repositoryFoundAndUpdatedDocument(logger, repositoryName, dbName, matched, null!);

        public static void RepositoryFoundDocument(this ILogger logger, string repositoryName, string dbName, string modelId) =>
            _repositoryFoundDocument(logger, repositoryName, dbName, modelId, null!);

        public static void RepositoryFoundMissingOriginReferences(this ILogger logger, string repositoryName, string dbName, long missingOriginIdsCount) =>
            _repositoryFoundMissingOriginReferences(logger, repositoryName, dbName, missingOriginIdsCount, null!);

        public static void RepositoryInitialized(this ILogger logger, string repositoryName, string dbName) =>
            _repositoryInitialized(logger, repositoryName, dbName, null!);

        public static void RepositoryMigratedDeprecatedSchemaIdDocuments(this ILogger logger, string repositoryName, string dbName, long migratedDocumentsCount, long documentErrorsCount) =>
            _repositoryMigratedDeprecatedSchemaIdDocuments(logger, repositoryName, dbName, migratedDocumentsCount, documentErrorsCount, null!);

        public static void RepositoryQueriedCollection(this ILogger logger, string repositoryName, string dbName) =>
            _repositoryQueriedCollection(logger, repositoryName, dbName, null!);

        public static void RepositoryRegistryInitialized(this ILogger logger, string dbName) =>
            _repositoryRegistryInitialized(logger, dbName, null!);

        public static void RepositoryRemovedMissingOriginReference(this ILogger logger, string repositoryName, string dbName, string elementPath, string missingOriginId) =>
            _repositoryRemovedMissingOriginReference(logger, repositoryName, dbName, missingOriginId, elementPath, null!);

        public static void RepositoryRemovedMissingOriginReferences(this ILogger logger, string repositoryName, string dbName, long missingOriginIdsCount, long updatedDocumentsCount) =>
            _repositoryRemovedMissingOriginReferences(logger, repositoryName, dbName, missingOriginIdsCount, updatedDocumentsCount, null!);

        public static void RepositoryReplacedDocument(this ILogger logger, string repositoryName, string dbName, string modelId) =>
            _repositoryReplacedDocument(logger, repositoryName, dbName, modelId, null!);

        public static void RepositorySavedModelChanges(this ILogger logger, string repositoryName, string dbName, string modelId) =>
            _repositorySavedModelChanges(logger, repositoryName, dbName, modelId, null!);

        public static void RepositorySaveFellBackToDocumentReplace(this ILogger logger, string repositoryName, string dbName, string modelId, string reason) =>
            _repositorySaveFellBackToDocumentReplace(logger, repositoryName, dbName, modelId, reason, null!);

        public static void RepositorySkippedDependenciesUpdate(this ILogger logger, string repositoryName, string dbName, string modelId) =>
            _repositorySkippedDependenciesUpdate(logger, repositoryName, dbName, modelId, null!);

        public static void RepositoryUpsertedDocument(this ILogger logger, string repositoryName, string dbName, bool inserted) =>
            _repositoryUpsertedDocument(logger, repositoryName, dbName, inserted, null!);

        public static void ResourceLockLeaseLost(this ILogger logger, string lockId, string ownerId) =>
            _resourceLockLeaseLost(logger, lockId, ownerId, null!);

        public static void ResourceLockLeaseRenewalFailed(this ILogger logger, string lockId, string ownerId, Exception exception) =>
            _resourceLockLeaseRenewalFailed(logger, lockId, ownerId, exception);

        public static void ResourceLockReleaseFailed(this ILogger logger, string lockId, string ownerId, Exception exception) =>
            _resourceLockReleaseFailed(logger, lockId, ownerId, exception);

        public static void SchemaRegistryInitialized(this ILogger logger, string dbName) =>
            _schemaRegistryInitialized(logger, dbName, null!);

        public static void UpdateDocDependenciesTaskEnded(this ILogger logger, Type dbContextType, string referencedRepositoryName, string modelId) =>
            _updateDocDependenciesTaskEnded(logger, dbContextType, referencedRepositoryName, modelId, null!);

        public static void UpdateDocDependenciesTaskSkippedOnDeletedModel(this ILogger logger, Type dbContextType, string referencedRepositoryName, string modelId) =>
            _updateDocDependenciesTaskSkippedOnDeletedModel(logger, dbContextType, referencedRepositoryName, modelId, null!);

        public static void UpdateDocDependenciesTaskSkippedOnUnknownRepository(this ILogger logger, Type dbContextType, string referencedRepositoryName) =>
            _updateDocDependenciesTaskSkippedOnUnknownRepository(logger, dbContextType, referencedRepositoryName, null!);

        public static void UpdateDocDependenciesTaskStarted(this ILogger logger, Type dbContextType, string referencedRepositoryName, string modelId, IEnumerable<string> idMemberMapIdentifiers) =>
            _updateDocDependenciesTaskStarted(logger, dbContextType, referencedRepositoryName, modelId, idMemberMapIdentifiers, null!);
    }
}
