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

using Etherna.Scrinium.Core.Tasks;
using Hangfire.Server;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace Etherna.Scrinium.HF.Tasks
{
    internal sealed class MigrateDbContextTaskFacade(IMigrateDbContextTask task)
    {
        // Methods.
        public Task RunAsync(Type dbContextType, string dbMigrationOpId, PerformContext context)
        {
            var method = typeof(MigrateDbContextTask).GetMethod(
                nameof(MigrateDbContextTask.RunAsync), BindingFlags.Public | BindingFlags.Instance)!
                .MakeGenericMethod(dbContextType);

            return (Task)method.Invoke(task, [dbMigrationOpId, context.BackgroundJob.Id])!;
        }
    }
}
