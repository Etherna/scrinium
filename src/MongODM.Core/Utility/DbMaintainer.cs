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
using Etherna.MongODM.Core.ProxyModels;
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.Core.Tasks;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;

namespace Etherna.MongODM.Core.Utility
{
    public class DbMaintainer(ITaskRunner taskRunner) : IDbMaintainer
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
        public void OnUpdatedModel<TKey>(IAuditable updatedModel, IRepository referenceRepository)
        {
            ArgumentNullException.ThrowIfNull(updatedModel);
            ArgumentNullException.ThrowIfNull(referenceRepository);
            if (updatedModel is not IEntityModel<TKey>)
                throw new ArgumentException($"Model is not of type {nameof(IEntityModel<TKey>)}", nameof(updatedModel));

            // Find all possibly involved member maps with changes, from all model maps. Select only referenced members.
            var referenceMemberMaps = updatedModel.ChangedMembers
                .SelectMany(updatedMemberInfo => dbContextEngine.MapRegistry.GetMemberMapsFromMemberInfo(updatedMemberInfo))
                .Where(memberMap => memberMap.IsEntityReferenceMember);

            // Find related id member maps.
            /*
             * idMemberMaps contains reference Ids for sub-documents summary of the updated document, containing updated property.
             * These are taken from all schemas and all model maps.
             */
            var idMemberMaps = referenceMemberMaps
                .Select(mm => mm.OwnerEntityIdMap!) //must exist, because we have selected only referenced member maps
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

            taskRunner.RunUpdateDocDependenciesTask(
                dbContextEngine.DbContextType,
                referenceRepository.Name,
                updatedModelId,
                idMemberMapIds,
                ExclusiveAccessHandler.IsExclusiveAccessAllowed(dbContextEngine.ExecutionContext));

            logger.DbMaintainerEnqueuedDependenciesUpdateTask(
                dbContextEngine.Options.DbName,
                dbContextEngine.ProxyGenerator.PurgeProxyType(updatedModel.GetType()),
                updatedModelId.ToString()!,
                idMemberMapIds.Length);
        }
    }
}
