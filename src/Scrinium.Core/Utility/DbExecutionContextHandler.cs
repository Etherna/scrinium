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

using Etherna.Scrinium.Core.ExecContext;
using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Etherna.Scrinium.Core.Extensions;
using Etherna.Scrinium.Core.Repositories;
using System;

namespace Etherna.Scrinium.Core.Utility
{
    /// <summary>
    /// Associates the current db operation to the async flow, for the scope of the handler:
    /// the engine running it, the db context scope when it has one, and the repository
    /// originating the models it materializes.
    /// </summary>
    /// <remarks>
    /// The handlers resolve on a stack local to the async flow: the current one is the last
    /// pushed by the flow itself, or inherited from the flow that forked it, never one pushed
    /// by a sibling flow of the same execution context, like the parallel tasks of one HTTP
    /// request sharing its items. A handler is visible from its creation to its disposal, and
    /// never past the end of the async method that created it.
    /// </remarks>
    public sealed class DbExecutionContextHandler : IDisposable
    {
        // Consts.
        private const string HandlerKey = "DbContextExecutionContextHandler";

        // Fields.
        private readonly IAsyncLocalContextHandler? asyncLocalContextHandler;
        private readonly FlowLocalStack<DbExecutionContextHandler> handlers;

        // Constructors and dispose.
        public DbExecutionContextHandler(
            IDbContext dbContext,
            IRepository? repository = null)
            : this(ExtractEngine(dbContext), repository)
        {
            DbContext = dbContext;
        }

        public DbExecutionContextHandler(
            IDbContextEngine dbContextEngine,
            IRepository? repository = null)
        {
            DbContextEngine = dbContextEngine ?? throw new ArgumentNullException(nameof(dbContextEngine));
            Repository = repository;

            var executionContext = dbContextEngine.ExecutionContext;

            /* If an execution context doesn't exist, create it: it registers the handlers stack,
             * and hosts the other ambient state of the operation. */
            if (executionContext.Items is null)
                asyncLocalContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            handlers = executionContext.GetOrAddFlowLocalStack<DbExecutionContextHandler>(HandlerKey);
            handlers.Push(this);
        }

        public void Dispose()
        {
            handlers.Remove(this);

            asyncLocalContextHandler?.Dispose();
        }

        // Properties.
        /// <summary>
        /// The db context scope running the operation, when the operation has one.
        /// Null when the handler covers engine level work, like schema registration.
        /// </summary>
        public IDbContext? DbContext { get; }
        public IDbContextEngine DbContextEngine { get; }

        /// <summary>
        /// The origin repository of the models materialized by the current operation:
        /// the repository accessing its collection, or the one configured on the
        /// reference member in deserialization. Null when not identified.
        /// </summary>
        public IRepository? Repository { get; }

        // Static methods.
        public static IDbContext? TryGetCurrentDbContext(IExecutionContext context) =>
            TryGetCurrentHandler(context)?.DbContext;

        public static IDbContextEngine? TryGetCurrentDbContextEngine(IExecutionContext context) =>
            TryGetCurrentHandler(context)?.DbContextEngine;

        public static IRepository? TryGetCurrentRepository(IExecutionContext context) =>
            TryGetCurrentHandler(context)?.Repository;

        // Helpers.
        private static IDbContextEngine ExtractEngine(IDbContext dbContext)
        {
            ArgumentNullException.ThrowIfNull(dbContext);
            return dbContext.Engine;
        }

        private static DbExecutionContextHandler? TryGetCurrentHandler(IExecutionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            //the last pushed by the flow, for recursing calls between different db contexts
            return context.TryGetFlowLocalStack<DbExecutionContextHandler>(HandlerKey)?.Top;
        }
    }
}
