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

using Etherna.Scrinium.Core.ExecContext;
using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Etherna.Scrinium.Core.Extensions;
using Etherna.Scrinium.Core.Repositories;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Etherna.Scrinium.Core.Utility
{
    public sealed class DbExecutionContextHandler : IDisposable
    {
        // Consts.
        private const string HandlerKey = "DbContextExecutionContextHandler";

        // Fields.
        private readonly IAsyncLocalContextHandler? asyncLocalContextHandler;
        private readonly ICollection<DbExecutionContextHandler> requests;

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

            if (executionContext.Items is null) //if an execution context doesn't exist, create it
                asyncLocalContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            requests = executionContext.GetOrAddItemsList<DbExecutionContextHandler>(HandlerKey);

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

            var requests = context.TryGetItemsList<DbExecutionContextHandler>(HandlerKey);
            if (requests is null)
                return null;

            //get the last with a stack system, for recursing calls between different dbContexts
            lock (((ICollection)requests).SyncRoot)
                return requests.Reverse().FirstOrDefault();
        }
    }
}
