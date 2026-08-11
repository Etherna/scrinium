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

using Etherna.MongODM.Core.ExecContext.Exceptions;
using Etherna.MongODM.Core.Extensions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Etherna.MongODM.Core.Utility
{
    internal sealed class ExclusiveAccessHandler : IDisposable
    {
        // Consts.
        private const string HandlerKey = "ExclusiveAccessHandler";

        // Fields.
        private readonly ICollection<ExclusiveAccessHandler> requests;

        // Constructors and dispose.
        public ExclusiveAccessHandler(IDbContextEngine dbContextEngine)
        {
            ArgumentNullException.ThrowIfNull(dbContextEngine);

            DbContextEngine = dbContextEngine;

            requests = dbContextEngine.ExecutionContext.GetOrAddItemsList<ExclusiveAccessHandler>(HandlerKey);

            lock (((ICollection)requests).SyncRoot)
                requests.Add(this);
        }

        public void Dispose()
        {
            lock (((ICollection)requests).SyncRoot)
                requests.Remove(this);
        }

        // Properties.
        public IDbContextEngine DbContextEngine { get; }

        // Static methods.
        public static bool IsExclusiveAccessAllowed(IDbContextEngine dbContextEngine)
        {
            ArgumentNullException.ThrowIfNull(dbContextEngine);

            var context = dbContextEngine.ExecutionContext;
            if (context.Items is null)
                throw new ExecutionContextNotFoundException();

            var requests = context.TryGetItemsList<ExclusiveAccessHandler>(HandlerKey);
            if (requests is null)
                return false;

            /* Exclusive access locks a single engine, while the execution context items are
             * shared by every db context of the flow: an allowance opens only the engine
             * that granted it. */
            lock (((ICollection)requests).SyncRoot)
                return requests.Any(handler => handler.DbContextEngine == dbContextEngine);
        }
    }
}