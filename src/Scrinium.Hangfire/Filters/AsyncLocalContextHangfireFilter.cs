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
    /// <summary>
    /// Opens an isolated async local context for each performance of a Hangfire job, disposed at its end.
    /// </summary>
    /// <remarks>
    /// The context handler travels in the items of the performance, shared by its performing and performed
    /// contexts. Hangfire performs a job at least once: a job whose fetch expired is handed to another worker,
    /// in another process or in the one still running it, so the context belongs to the performance, never
    /// to the job id.
    /// </remarks>
    public class AsyncLocalContextHangfireFilter(IAsyncLocalContext asyncLocalContext) : IServerFilter
    {
        // Consts.
        private const string ContextHandlerItemKey = "Scrinium.AsyncLocalContextHandler";

        // Methods.
        public void OnPerformed(PerformedContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (context.Items.Remove(ContextHandlerItemKey, out var contextHandler))
            {
                ((IAsyncLocalContextHandler)contextHandler).Dispose();
            }
        }

        public void OnPerforming(PerformingContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            context.Items[ContextHandlerItemKey] = asyncLocalContext.InitAsyncLocalContext();
        }
    }
}
