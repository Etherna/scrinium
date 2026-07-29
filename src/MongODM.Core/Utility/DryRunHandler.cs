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
using Etherna.MongODM.Core.ExecContext.Exceptions;
using System;
using System.Collections;
using System.Collections.Generic;

namespace Etherna.MongODM.Core.Utility
{
    /* Marks the current execution flow as a dry run: collection writes execute their client
     * side work (definition rendering and document serialization) but are not sent to the
     * server, returning simulated results, and dependent documents propagation is skipped. */
    internal sealed class DryRunHandler : IDisposable
    {
        // Consts.
        private const string HandlerKey = "DryRunHandler";

        // Fields.
        private readonly ICollection<DryRunHandler> requests;

        // Constructors and dispose.
        public DryRunHandler(IExecutionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (context.Items is null)
                throw new ExecutionContextNotFoundException();

            if (!context.Items.ContainsKey(HandlerKey))
                context.Items.Add(HandlerKey, new List<DryRunHandler>());

            requests = (ICollection<DryRunHandler>)context.Items[HandlerKey]!;

            lock (((ICollection)requests).SyncRoot)
                requests.Add(this);
        }

        public void Dispose()
        {
            lock (((ICollection)requests).SyncRoot)
                requests.Remove(this);
        }

        // Static methods.
        public static bool IsDryRunEnabled(IExecutionContext context)
        {
            if (context.Items is null)
                throw new ExecutionContextNotFoundException();

            if (!context.Items.TryGetValue(HandlerKey, out var requestsObj))
                return false;
            var requests = (ICollection<DryRunHandler>)requestsObj!;

            lock (((ICollection)requests).SyncRoot)
                return requests.Count != 0;
        }
    }
}
