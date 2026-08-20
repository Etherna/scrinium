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

using Etherna.MongODM.Core.Options;
using Etherna.MongODM.Core.Tasks;
using Hangfire;
using Hangfire.States;
using System;
using System.Collections.Generic;

namespace Etherna.MongODM.HF.Tasks
{
    public sealed class HangfireTaskRunner(IBackgroundJobClient backgroundJobClient)
        : ITaskRunner, ITaskRunnerBuilder
    {
        // Fields.
        private MongODMOptions mongODMOptions = null!;

        // Methods.
        public void RunDeleteDocDependenciesTask(
            Type dbContextType,
            Type deletedDbContextType,
            string deletedRepositoryName,
            object modelId,
            IEnumerable<string> idMemberMapIdentifiers) =>
            backgroundJobClient.Create<DeleteDocDependenciesTaskFacade>(
                task => task.RunAsync(
                    dbContextType,
                    deletedDbContextType,
                    deletedRepositoryName,
                    modelId,
                    idMemberMapIdentifiers),
                new EnqueuedState(mongODMOptions.DbMaintenanceQueueName));

        public void RunMigrateDbTask(Type dbContextType, string dbMigrationOpId) =>
            backgroundJobClient.Create<MigrateDbContextTaskFacade>(
                task => task.RunAsync(dbContextType, dbMigrationOpId, null!),
                new EnqueuedState(mongODMOptions.DbMaintenanceQueueName));

        public void RunUpdateDocDependenciesTask(
            Type dbContextType,
            Type referenceDbContextType,
            string referenceRepositoryName,
            object modelId,
            IEnumerable<string> idMemberMapIdentifiers) =>
            backgroundJobClient.Create<UpdateDocDependenciesTaskFacade>(
                task => task.RunAsync(
                    dbContextType,
                    referenceDbContextType,
                    referenceRepositoryName,
                    modelId,
                    idMemberMapIdentifiers),
                new EnqueuedState(mongODMOptions.DbMaintenanceQueueName));

        // Explicit methods.
        void ITaskRunnerBuilder.SetMongODMOptions(MongODMOptions options) =>
            mongODMOptions = options;
    }
}
