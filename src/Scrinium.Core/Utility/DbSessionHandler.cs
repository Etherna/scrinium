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

using Etherna.MongoDB.Driver;
using Etherna.Scrinium.Core.ExecContext;
using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Etherna.Scrinium.Core.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Etherna.Scrinium.Core.Utility
{
    /// <summary>
    /// Associates a database session to the current execution context flow, for the scope
    /// of the handler. While the handler is active, operations invoked without an explicit
    /// session on collections of the same engine enlist automatically in the handled
    /// session, joining its transaction when one is active, and defer to its outcome the
    /// bookkeeping of their in memory effects: applied at the commit, or undone at the abort.
    /// </summary>
    /// <remarks>
    /// The handlers resolve on a stack local to the async flow, like the db execution context
    /// handlers: the handled session enlists the operations of the flow, and of the flows it
    /// forks inside the scope, never the ones of a sibling flow of the same execution context.
    /// Database sessions don't support concurrent operations: keep operations sequential
    /// inside the handler scope.
    /// </remarks>
    public sealed class DbSessionHandler : IDisposable
    {
        // Consts.
        private const string HandlerKey = "DbSessionHandler";

        // Fields.
        private readonly List<Action> abortActions = [];
        private readonly IAsyncLocalContextHandler? asyncLocalContextHandler;
        private readonly List<Action> commitActions = [];
        private readonly FlowLocalStack<DbSessionHandler> handlers;

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

            handlers = executionContext.GetOrAddFlowLocalStack<DbSessionHandler>(HandlerKey);
            handlers.Push(this);
        }

        public void Dispose()
        {
            handlers.Remove(this);

            asyncLocalContextHandler?.Dispose();
        }

        // Properties.
        public IDbContextEngine DbContextEngine { get; }
        public IClientSessionHandle Session { get; }

        // Static methods.
        public static IClientSessionHandle? TryGetCurrentSession(IDbContextEngine dbContextEngine) =>
            TryGetCurrentHandler(dbContextEngine)?.Session;

        // Internals.
        /// <summary>
        /// Run the undo the enlisted operations deferred to the abort of the handled
        /// transaction, in reverse registration order, once the transaction aborted or
        /// failed its commit. The bookkeeping deferred to the commit drops.
        /// </summary>
        internal void RunAbortActions()
        {
            commitActions.Clear();
            for (var i = abortActions.Count - 1; i >= 0; i--)
                abortActions[i]();
            abortActions.Clear();
        }

        /// <summary>
        /// Run the bookkeeping the enlisted operations deferred to the commit of the handled
        /// transaction, in registration order, once the transaction committed. The undo
        /// deferred to the abort drops.
        /// </summary>
        internal void RunCommitActions()
        {
            abortActions.Clear();
            foreach (var action in commitActions)
                action();
            commitActions.Clear();
        }

        /// <summary>
        /// Defer the undo of an operation in memory effects to the abort of the ambient
        /// transaction of the engine. Without an ambient session handler nothing defers:
        /// without a transaction the operation persisted, and there is nothing to undo.
        /// </summary>
        /// <param name="dbContextEngine">The engine of the enlisted operation</param>
        /// <param name="action">The undo to run at abort</param>
        /// <returns>True if the action deferred to the abort</returns>
        internal static bool TryDeferToTransactionAbort(IDbContextEngine dbContextEngine, Action action)
        {
            ArgumentNullException.ThrowIfNull(action);

            if (TryGetCurrentHandler(dbContextEngine) is not { } currentHandler)
                return false;

            currentHandler.abortActions.Add(action);
            return true;
        }

        /// <summary>
        /// Defer an operation bookkeeping to the commit of the ambient transaction of the
        /// engine. Without an ambient session handler nothing defers, and the bookkeeping
        /// stays with the caller.
        /// </summary>
        /// <param name="dbContextEngine">The engine of the enlisted operation</param>
        /// <param name="action">The bookkeeping to run at commit</param>
        /// <returns>True if the action deferred to the commit</returns>
        internal static bool TryDeferToTransactionCommit(IDbContextEngine dbContextEngine, Action action)
        {
            ArgumentNullException.ThrowIfNull(action);

            if (TryGetCurrentHandler(dbContextEngine) is not { } currentHandler)
                return false;

            currentHandler.commitActions.Add(action);
            return true;
        }

        // Helpers.
        private static DbSessionHandler? TryGetCurrentHandler(IDbContextEngine dbContextEngine)
        {
            ArgumentNullException.ThrowIfNull(dbContextEngine);

            /* Get the last handler of the same engine pushed by the flow, for nesting sessions
             * between different db contexts. Sessions are per connection: handlers of other
             * engines don't apply. */
            return dbContextEngine.ExecutionContext.TryGetFlowLocalStack<DbSessionHandler>(HandlerKey)?
                .Items.FirstOrDefault(handler => handler.DbContextEngine == dbContextEngine);
        }
    }
}
