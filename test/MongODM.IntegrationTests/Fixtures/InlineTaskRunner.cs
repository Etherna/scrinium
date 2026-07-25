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
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Etherna.MongODM.IntegrationTests.Fixtures
{
    /// <summary>
    /// Collects the tasks enqueued by MongODM components, and executes them on demand inside
    /// a dedicated DI scope, like the production task executor would do in background.
    /// </summary>
    internal sealed class InlineTaskRunner : ITaskRunner, ITaskRunnerBuilder
    {
        // Fields.
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

        // Methods.
        public void ClearPending()
        {
            lock (pendingTasks)
                pendingTasks.Clear();
        }

        public async Task ExecutePendingAsync(IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider);

            List<Func<IServiceProvider, Task>> tasks;
            lock (pendingTasks)
            {
                tasks = [.. pendingTasks];
                pendingTasks.Clear();
            }

            foreach (var task in tasks)
            {
                using var scope = serviceProvider.CreateScope();
                await task(scope.ServiceProvider);
            }
        }

        public void RunMigrateDbTask(Type dbContextType, string dbMigrationOpId) { }

        public void RunUpdateDocDependenciesTask(
            Type dbContextType,
            string referenceRepositoryName,
            object modelId,
            IEnumerable<string> idMemberMapIdentifiers)
        {
            ArgumentNullException.ThrowIfNull(idMemberMapIdentifiers);

            //materialize before deferred execution
            var idMemberMapIdentifiersList = idMemberMapIdentifiers.ToArray();

            lock (pendingTasks)
                pendingTasks.Add(serviceProvider =>
                    (Task)typeof(IUpdateDocDependenciesTask)
                        .GetMethod(nameof(IUpdateDocDependenciesTask.RunAsync))!
                        .MakeGenericMethod(dbContextType)
                        .Invoke(
                            serviceProvider.GetRequiredService<IUpdateDocDependenciesTask>(),
                            [referenceRepositoryName, modelId, idMemberMapIdentifiersList])!);
        }

        public void SetMongODMOptions(MongODMOptions options) { }
    }
}
