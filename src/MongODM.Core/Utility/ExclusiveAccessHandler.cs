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

using Etherna.ExecContext;
using Etherna.ExecContext.Exceptions;
using System;
using System.Collections;
using System.Collections.Generic;

namespace Etherna.MongODM.Core.Utility
{
    internal sealed class ExclusiveAccessHandler : IDisposable
    {
        // Consts.
        private const string HandlerKey = "ExclusiveAccessHandler";

        // Fields.
        private readonly ICollection<ExclusiveAccessHandler> requests;

        // Constructors and dispose.
        public ExclusiveAccessHandler(IExecutionContext context)
        {
            ArgumentNullException.ThrowIfNull(context, nameof(context));
            if (context.Items is null)
                throw new ExecutionContextNotFoundException();

            if (!context.Items.ContainsKey(HandlerKey))
                context.Items.Add(HandlerKey, new List<ExclusiveAccessHandler>());

            requests = (ICollection<ExclusiveAccessHandler>)context.Items[HandlerKey]!;

            lock (((ICollection)requests).SyncRoot)
                requests.Add(this);
        }

        public void Dispose()
        {
            lock (((ICollection)requests).SyncRoot)
                requests.Remove(this);
        }

        // Static methods.
        public static bool IsExclusiveAccessAllowed(IExecutionContext context)
        {
            if (context.Items is null)
                throw new ExecutionContextNotFoundException();

            if (!context.Items.TryGetValue(HandlerKey, out var requestsObj))
                return false;
            var requests = (ICollection<ExclusiveAccessHandler>)requestsObj!;

            lock (((ICollection)requests).SyncRoot)
                return requests.Count != 0;
        }
    }
}