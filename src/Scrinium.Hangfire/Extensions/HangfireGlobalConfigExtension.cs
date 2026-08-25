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
using Etherna.Scrinium.HF.Filters;

namespace Hangfire
{
    public static class HangfireGlobalConfigExtension
    {
#pragma warning disable IDE0060 // Remove unused parameter
        public static void UseScrinium(this IGlobalConfiguration config)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            // Add a default execution context running with any Hangfire task.
            // Added because with asyncronous task, unrelated to requests, there is no an alternative context to use with Scrinium.
            GlobalJobFilters.Filters.Add(new AsyncLocalContextHangfireFilter(AsyncLocalContext.Instance));
        }
    }
}
