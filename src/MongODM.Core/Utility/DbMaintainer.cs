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

using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.Extensions;
using Etherna.MongODM.Core.Options;
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.Core.Serialization.Mapping;
using Etherna.MongODM.Core.Serialization.Serializers;
using Etherna.MongODM.Core.Tasks;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Etherna.MongODM.Core.Utility
{
    public class DbMaintainer(
        IParentEnginesProvider parentEnginesProvider,
        ITaskRunner taskRunner) : IDbMaintainer
    {
        // Fields.
        private IDbContextEngine dbContextEngine = null!;
        private ILogger logger = null!;

        // Initializer.
        public void Initialize(IDbContextEngine dbContextEngine, ILogger logger)
        {
            if (IsInitialized)
                throw new InvalidOperationException("Instance already initialized");

            this.dbContextEngine = dbContextEngine ?? throw new ArgumentNullException(nameof(dbContextEngine));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

            IsInitialized = true;

            this.logger.DbMaintainerInitialized(dbContextEngine.Options.DbName);
        }

        // Properties.
        public bool IsInitialized { get; private set; }

        // Methods.
        public void OnDeletedModel<TKey>(IEntityModel deletedModel, IRepository referenceRepository)
        {
            ArgumentNullException.ThrowIfNull(deletedModel);
            ArgumentNullException.ThrowIfNull(referenceRepository);
            if (deletedModel is not IEntityModel<TKey>)
                throw new ArgumentException($"Model is not of type {nameof(IEntityModel<TKey>)}", nameof(deletedModel));

            var deletedModelId = ((IEntityModel<TKey>)deletedModel).Id!;

            // Skip the propagation on a dry run: simulated writes don't alter any document.
            if (DryRunHandler.IsDryRunEnabled(dbContextEngine.ExecutionContext))
            {
                logger.DbMaintainerSkippedDependenciesDeleteOnDryRun(
                    dbContextEngine.Options.DbName,
                    deletedModelId.ToString()!);
                return;
            }

            // Find the reference id member maps declaring a delete propagation policy on the model.
            /*
             * Candidates are the reference id members whose reference serializer declares an
             * origin delete policy other than keeping the reference, can host the deleted model
             * type, and resolves its source on the repository the model was deleted from:
             * hierarchically dependent origin repositories can host the same model type, and a
             * reference sourced elsewhere doesn't point at the deleted document.
             */
            var deletedModelType = dbContextEngine.ProxyGenerator.PurgeProxyType(deletedModel.GetType());
            var idMemberMapIds = dbContextEngine.MapRegistry.MemberMapsById.Values
                .Where(memberMap => memberMap is { IsEntityReferenceMember: true, IsIdMember: true })
                .Where(memberMap =>
                    memberMap.TryFindHostingReferenceSerializer() is { } referenceSerializer &&
                    referenceSerializer.Configuration.OriginDelete != OriginDeleteMode.KeepReference &&
                    referenceSerializer.ReferenceModelType.IsAssignableFrom(deletedModelType) &&
                    referenceSerializer.TryResolveSourceRepository(referenceRepository.DbContext) == referenceRepository)
                .Select(memberMap => memberMap.Id)
                .ToArray();

            var enqueuedTasks = 0;
            if (idMemberMapIds.Length > 0)
            {
                taskRunner.RunDeleteDocDependenciesTask(
                    dbContextEngine.DbContextType,
                    dbContextEngine.DbContextType,
                    referenceRepository.Name,
                    deletedModelId,
                    idMemberMapIds);
                enqueuedTasks++;

                logger.DbMaintainerEnqueuedDependenciesDeleteTask(
                    dbContextEngine.Options.DbName,
                    deletedModelType,
                    deletedModelId.ToString()!,
                    idMemberMapIds.Length);
            }

            // Enqueue the propagation to the parent engines referencing the deleted model.
            /*
             * Cross db context references live in the map registries of the parent db
             * contexts: their id member maps select there with the same policy filters,
             * keeping only the references sourced on the deleting repository, and each
             * involved parent engine gets a task of its own. The propagation crosses the
             * engines of this application only: documents of applications not hosting the
             * parent db context keep their references, found by the missing origin
             * references scan.
             */
            foreach (var parentEngine in parentEnginesProvider.GetParentEngines(dbContextEngine.DbContextType))
            {
                /* A read-only parent db context consumes documents owned by another
                 * application: they are not this propagation's to repair, and every write
                 * would be denied. */
                if (parentEngine.Options.IsReadOnly)
                    continue;

                var parentIdMemberMapIds = parentEngine.MapRegistry.MemberMapsById.Values
                    .Where(memberMap => memberMap is { IsEntityReferenceMember: true, IsIdMember: true })
                    .Where(memberMap =>
                        memberMap.TryFindHostingReferenceSerializer() is { } referenceSerializer &&
                        referenceSerializer.Configuration.OriginDelete != OriginDeleteMode.KeepReference &&
                        referenceSerializer.ReferenceModelType.IsAssignableFrom(deletedModelType) &&
                        IsSourcedOnRepository(referenceSerializer, referenceRepository))
                    .Select(memberMap => memberMap.Id)
                    .ToArray();
                if (parentIdMemberMapIds.Length == 0)
                    continue;

                taskRunner.RunDeleteDocDependenciesTask(
                    parentEngine.DbContextType,
                    dbContextEngine.DbContextType,
                    referenceRepository.Name,
                    deletedModelId,
                    parentIdMemberMapIds);
                enqueuedTasks++;

                logger.DbMaintainerEnqueuedParentDependenciesDeleteTask(
                    dbContextEngine.Options.DbName,
                    parentEngine.Options.DbName,
                    deletedModelType,
                    deletedModelId.ToString()!,
                    parentIdMemberMapIds.Length);
            }

            // Skip the enqueue without involved references: the tasks would have nothing to propagate.
            if (enqueuedTasks == 0)
                logger.DbMaintainerSkippedDependenciesDeleteWithoutPolicies(
                    dbContextEngine.Options.DbName,
                    deletedModelId.ToString()!);
        }

        /*
         * Maintain summary information from origin summary documents, pointing to updated referenced document.
         * 
         * Example:
         * originDoc1: {
         *   "a": {
         *     "_id": "referredDocId",
         *     "c": "cVal"
         *   }
         * }
         * originDoc2:{
         *   "b": {
         *     "_id": "referredDocId",
         *     "d": "dVal"
         *   }
         * }
         * referredDoc:{
         *   "_id": "referredDocId",
         *   "c": "cVal",
         *   "d": "dVal"
         * }
         * 
         * If referred document "referredDoc" updates it's fields "b" and "c" with a new value,
         * "originDoc1.a" and "originDoc2.b" fields would be updated by this process.
         */
        public void OnUpdatedModel<TKey>(IEntityModel updatedModel, IEnumerable<MemberInfo> changedMembers, IRepository referenceRepository)
        {
            ArgumentNullException.ThrowIfNull(updatedModel);
            ArgumentNullException.ThrowIfNull(changedMembers);
            ArgumentNullException.ThrowIfNull(referenceRepository);
            if (updatedModel is not IEntityModel<TKey>)
                throw new ArgumentException($"Model is not of type {nameof(IEntityModel<TKey>)}", nameof(updatedModel));

            // Skip the propagation on a dry run: simulated writes don't alter any document.
            if (DryRunHandler.IsDryRunEnabled(dbContextEngine.ExecutionContext))
            {
                logger.DbMaintainerSkippedDependenciesUpdateOnDryRun(
                    dbContextEngine.Options.DbName,
                    ((IEntityModel<TKey>)updatedModel).Id!.ToString()!);
                return;
            }

            //materialize the changed members once: the parent engines enumerate them again
            var changedMembersList = changedMembers.ToArray();

            // Find all possibly involved member maps with changes, from all model maps. Select only referenced members.
            var referenceMemberMaps = changedMembersList
                .SelectMany(updatedMemberInfo => dbContextEngine.MapRegistry.GetMemberMapsFromMemberInfo(updatedMemberInfo))
                .Where(memberMap => memberMap.IsEntityReferenceMember);

            // Find related id member maps.
            /*
             * idMemberMaps contains reference Ids for sub-documents summary of the updated document, containing updated property.
             * These are taken from all schemas and all model maps.
             */
            var idMemberMaps = referenceMemberMaps
                .Select(mm => mm.OwnerEntityIdMap)
                .OfType<IMemberMap>() //members of schemas without an id of their own resolve no owner id
                .Distinct();

            // Select all id member maps with same element path of previously selected.
            /*
             * We need to keep all id member maps with same element path, even if these new doesn't have any reference data involved in changes.
             * Reason of this is that when we choose how to serialize a proper subdocument, we need to have all possibility in hand.
             * Otherwise, if for example active schema serialize only reference Id, it will be never considered has a valid serialization schema by task.
             */
            var allIdMemberMaps = idMemberMaps
                .SelectMany(dbContextEngine.MapRegistry.GetMemberMapsWithSameElementPath)
                .Distinct();

            // Enqueue call of background job.
            /*
             * We pass member maps' string ids because strings are better serializable by the task executor.
             * All member maps must be recovered by the task using Ids from the schema register.
             */
            var idMemberMapIds = allIdMemberMaps.Select(mm => mm.Id).ToArray();
            var updatedModelId = ((IEntityModel<TKey>)updatedModel).Id!;

            var enqueuedTasks = 0;
            if (idMemberMapIds.Length > 0)
            {
                taskRunner.RunUpdateDocDependenciesTask(
                    dbContextEngine.DbContextType,
                    dbContextEngine.DbContextType,
                    referenceRepository.Name,
                    updatedModelId,
                    idMemberMapIds);
                enqueuedTasks++;

                logger.DbMaintainerEnqueuedDependenciesUpdateTask(
                    dbContextEngine.Options.DbName,
                    dbContextEngine.ProxyGenerator.PurgeProxyType(updatedModel.GetType()),
                    updatedModelId.ToString()!,
                    idMemberMapIds.Length);
            }

            // Enqueue the propagation to the parent engines denormalizing the changed members.
            /*
             * Cross db context references live in the map registries of the parent db
             * contexts, with their summaries denormalized into the parent documents: the
             * changed members resolve their member maps on each parent registry like on the
             * own one, keeping only the references sourced on the updated repository, and
             * each involved parent engine gets a task of its own. The propagation crosses
             * the engines of this application only: documents of applications not hosting
             * the parent db context keep their summaries, so cross db context summaries
             * denormalize safely only when every writer of the child collections also hosts
             * the referencing db contexts.
             */
            foreach (var parentEngine in parentEnginesProvider.GetParentEngines(dbContextEngine.DbContextType))
            {
                /* A read-only parent db context consumes documents owned by another
                 * application: their summaries are not this propagation's to refresh, and
                 * every write would be denied. */
                if (parentEngine.Options.IsReadOnly)
                    continue;

                var parentIdMemberMapIds = changedMembersList
                    .SelectMany(updatedMemberInfo => parentEngine.MapRegistry.GetMemberMapsFromMemberInfo(updatedMemberInfo))
                    .Where(memberMap => memberMap.IsEntityReferenceMember)
                    .Select(memberMap => memberMap.OwnerEntityIdMap)
                    .OfType<IMemberMap>()
                    .Distinct()
                    .Where(idMemberMap =>
                        idMemberMap.TryFindHostingReferenceSerializer() is { } referenceSerializer &&
                        IsSourcedOnRepository(referenceSerializer, referenceRepository))
                    .SelectMany(parentEngine.MapRegistry.GetMemberMapsWithSameElementPath)
                    .Distinct()
                    .Select(memberMap => memberMap.Id)
                    .ToArray();
                if (parentIdMemberMapIds.Length == 0)
                    continue;

                taskRunner.RunUpdateDocDependenciesTask(
                    parentEngine.DbContextType,
                    dbContextEngine.DbContextType,
                    referenceRepository.Name,
                    updatedModelId,
                    parentIdMemberMapIds);
                enqueuedTasks++;

                logger.DbMaintainerEnqueuedParentDependenciesUpdateTask(
                    dbContextEngine.Options.DbName,
                    parentEngine.Options.DbName,
                    dbContextEngine.ProxyGenerator.PurgeProxyType(updatedModel.GetType()),
                    updatedModelId.ToString()!,
                    parentIdMemberMapIds.Length);
            }

            // Skip the enqueue without involved reference members: the tasks would have nothing to propagate.
            if (enqueuedTasks == 0)
                logger.DbMaintainerSkippedDependenciesUpdateWithoutReferences(
                    dbContextEngine.Options.DbName,
                    updatedModelId.ToString()!);
        }

        // Helpers.
        /*
         * A cross db context reference declares its source db context type with the typed
         * factory: only such a selector can run on the changed repository db context,
         * resolving the source like a deserialization would. Selectors of untyped or
         * implicit sources expect an instance of their own db context type, so they never
         * source another engine.
         */
        private static bool IsSourcedOnRepository(IReferenceSerializer referenceSerializer, IRepository referenceRepository) =>
            referenceSerializer.SourceRepositoryDbContextType is { } sourceDbContextType &&
            sourceDbContextType.IsInstanceOfType(referenceRepository.DbContext) &&
            referenceSerializer.TryResolveSourceRepository(referenceRepository.DbContext) == referenceRepository;
    }
}
