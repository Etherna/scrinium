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

using Etherna.MongoDB.Driver;
using Etherna.MongODM.Core.ExecContext.AsyncLocal;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Etherna.MongODM.Core.Utility
{
    /// <summary>
    /// Associates a database session to the current execution context flow, for the scope
    /// of the handler. While the handler is active, operations invoked without an explicit
    /// session on collections of the same engine enlist automatically in the handled
    /// session, joining its transaction when one is active.
    /// </summary>
    /// <remarks>
    /// Database sessions don't support concurrent operations: keep operations sequential
    /// inside the handler scope.
    /// </remarks>
    public sealed class DbSessionHandler : IDisposable
    {
        // Consts.
        private const string HandlerKey = "DbSessionHandler";

        // Fields.
        private readonly IAsyncLocalContextHandler? asyncLocalContextHandler;
        private readonly ICollection<DbSessionHandler> requests;

        // Constructors and dispose.
        public DbSessionHandler(
            IDbContextEngine dbContextEngine,
            IClientSessionHandle session)
        {
            DbContextEngine = dbContextEngine ?? throw new ArgumentNullException(nameof(dbContextEngine));
            Session = session ?? throw new ArgumentNullException(nameof(session));

            var executionContext = dbContextEngine.ExecutionContext;

            if (executionContext.Items is null) //if an execution context doesn't exist, create it
                asyncLocalContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            if (!executionContext.Items!.ContainsKey(HandlerKey))
                executionContext.Items.Add(HandlerKey, new List<DbSessionHandler>());

            requests = (ICollection<DbSessionHandler>)executionContext.Items[HandlerKey]!;

            lock (((ICollection)requests).SyncRoot)
                requests.Add(this);
        }

        public void Dispose()
        {
            lock (((ICollection)requests).SyncRoot)
                requests.Remove(this);

            asyncLocalContextHandler?.Dispose();
        }

        // Properties.
        public IDbContextEngine DbContextEngine { get; }
        public IClientSessionHandle Session { get; }

        // Static methods.
        public static IClientSessionHandle? TryGetCurrentSession(IDbContextEngine dbContextEngine)
        {
            ArgumentNullException.ThrowIfNull(dbContextEngine);

            var executionContext = dbContextEngine.ExecutionContext;
            if (executionContext.Items is null ||
                !executionContext.Items.TryGetValue(HandlerKey, out var requestsObj))
                return null;
            var requests = (ICollection<DbSessionHandler>)requestsObj!;

            /* Get the last handler of the same engine with a stack system, for nesting
             * sessions between different db contexts. Sessions are per connection: handlers
             * of other engines don't apply. */
            lock (((ICollection)requests).SyncRoot)
                return requests
                    .Where(handler => handler.DbContextEngine == dbContextEngine)
                    .Reverse()
                    .FirstOrDefault()
                    ?.Session;
        }
    }
}
