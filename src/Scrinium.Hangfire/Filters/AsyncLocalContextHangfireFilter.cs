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

using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Hangfire.Server;
using System;
using System.Collections.Generic;

namespace Etherna.Scrinium.HF.Filters
{
    public class AsyncLocalContextHangfireFilter(IAsyncLocalContext asyncLocalContext) : IServerFilter
    {
        // Fields.
        private readonly Dictionary<string, IAsyncLocalContextHandler> contextHandlers = new();

        // Methods.
        public void OnPerformed(PerformedContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            lock (contextHandlers)
            {
                var jobId = context.BackgroundJob.Id;
                var contextHandler = contextHandlers[jobId];
                contextHandlers.Remove(jobId);
                contextHandler.Dispose();
            }
        }

        public void OnPerforming(PerformingContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            lock (contextHandlers)
            {
                contextHandlers.Add(context.BackgroundJob.Id, asyncLocalContext.InitAsyncLocalContext());
            }
        }
    }
}
