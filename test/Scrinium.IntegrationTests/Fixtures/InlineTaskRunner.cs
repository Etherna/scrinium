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

using Etherna.Scrinium.Core.Options;
using Etherna.Scrinium.Core.Tasks;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Etherna.Scrinium.IntegrationTests.Fixtures
{
    /// <summary>
    /// Collects the tasks enqueued by MongODM components, and executes them on demand inside
    /// a dedicated DI scope, like the production task executor would do in background.
    /// </summary>
    internal sealed class InlineTaskRunner : ITaskRunner, ITaskRunnerBuilder
    {
        // Fields.
        private readonly List<object> pendingModelIds = [];
        private readonly List<Func<IServiceProvider, Task>> pendingTasks = [];

        // Properties.
        public int PendingCount
        {
            get
            {
                lock (pendingTasks)
                    return pendingTasks.Count;
            }
        }

        /// <summary>
        /// The model ids of the pending dependencies propagation tasks.
        /// </summary>
        public IReadOnlyCollection<object> PendingModelIds
        {
            get
            {
                lock (pendingTasks)
                    return [.. pendingModelIds];
            }
        }

        // Methods.
        public void ClearPending()
        {
            lock (pendingTasks)
            {
                pendingModelIds.Clear();
                pendingTasks.Clear();
            }
        }

        public async Task ExecutePendingAsync(IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider);

            List<Func<IServiceProvider, Task>> tasks;
            lock (pendingTasks)
            {
                tasks = [.. pendingTasks];
                pendingModelIds.Clear();
                pendingTasks.Clear();
            }

            foreach (var task in tasks)
            {
                using var scope = serviceProvider.CreateScope();
                await task(scope.ServiceProvider);
            }
        }

        public void RunDeleteDocDependenciesTask(
            Type dbContextType,
            Type deletedDbContextType,
            string deletedRepositoryName,
            object modelId,
            IEnumerable<string> idMemberMapIdentifiers)
        {
            ArgumentNullException.ThrowIfNull(idMemberMapIdentifiers);

            //materialize before deferred execution
            var idMemberMapIdentifiersList = idMemberMapIdentifiers.ToArray();

            lock (pendingTasks)
            {
                pendingModelIds.Add(modelId);
                pendingTasks.Add(serviceProvider =>
                    (Task)typeof(IDeleteDocDependenciesTask)
                        .GetMethod(nameof(IDeleteDocDependenciesTask.RunAsync))!
                        .MakeGenericMethod(dbContextType)
                        .Invoke(
                            serviceProvider.GetRequiredService<IDeleteDocDependenciesTask>(),
                            [deletedDbContextType, deletedRepositoryName, modelId, idMemberMapIdentifiersList])!);
            }
        }

        public void RunMigrateDbTask(Type dbContextType, string dbMigrationOpId) { }

        public void RunUpdateDocDependenciesTask(
            Type dbContextType,
            Type referenceDbContextType,
            string referenceRepositoryName,
            object modelId,
            IEnumerable<string> idMemberMapIdentifiers)
        {
            ArgumentNullException.ThrowIfNull(idMemberMapIdentifiers);

            //materialize before deferred execution
            var idMemberMapIdentifiersList = idMemberMapIdentifiers.ToArray();

            lock (pendingTasks)
            {
                pendingModelIds.Add(modelId);
                pendingTasks.Add(serviceProvider =>
                    (Task)typeof(IUpdateDocDependenciesTask)
                        .GetMethod(nameof(IUpdateDocDependenciesTask.RunAsync))!
                        .MakeGenericMethod(dbContextType)
                        .Invoke(
                            serviceProvider.GetRequiredService<IUpdateDocDependenciesTask>(),
                            [referenceDbContextType, referenceRepositoryName, modelId, idMemberMapIdentifiersList])!);
            }
        }

        public void SetScriniumOptions(ScriniumOptions options) { }
    }
}
