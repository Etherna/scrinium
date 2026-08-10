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

using Etherna.MongODM.Core.ExecContext;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Etherna.MongODM.AspNetCore.ExecContext
{
    /// <summary>
    /// Execution context of the driver static hooks, delegating to the execution context that the
    /// application registered. The hooks are configured with the service collection, before the
    /// application service provider exists: the delegate resolves from it when the first db
    /// context engine is built, and until then the context reports no items.
    /// </summary>
    internal sealed class DeferredExecutionContext : IExecutionContext
    {
        // Fields.
        private IExecutionContext? source;

        // Properties.
        public IDictionary<object, object?>? Items => source?.Items;

        // Static properties.
        /// <summary>
        /// The instance feeding the driver static hooks, process wide as they are.
        /// </summary>
        public static DeferredExecutionContext Instance { get; } = new();

        // Methods.
        /// <summary>
        /// Bind the execution context resolved from the application service provider. Only the
        /// first binding applies: every db context engine of an application shares the same
        /// execution context instance.
        /// </summary>
        /// <param name="executionContext">The application execution context</param>
        public void Bind(IExecutionContext executionContext)
        {
            ArgumentNullException.ThrowIfNull(executionContext);

            Interlocked.CompareExchange(ref source, executionContext, null);
        }
    }
}
