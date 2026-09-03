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

using System;
using System.Collections.Generic;

namespace Etherna.Scrinium.Core.Options
{
    public interface IDbContextOptions
    {
        /// <summary>
        /// The db context types declared as children with <see cref="DbContextOptions.ParentFor{TDbContext}"/>:
        /// attached to each scoped instance, saved by its <see cref="IDbContext.SaveChangesAsync"/>,
        /// and hosting the sources of its cross db context references.
        /// </summary>
        public IEnumerable<Type> ChildDbContextTypes { get; }

        public string ConnectionString { get; }

        /// <summary>
        /// Name of the collection persisting the db context lock: the server side lease
        /// document coordinating the exclusive works of the db context (seeding and
        /// migrations) once per db context across every application instance connected to the
        /// database. Applications configuring different collection names for the same database
        /// don't exclude each other.
        /// </summary>
        public string DbLockCollectionName { get; }

        /// <summary>
        /// The database name declared as path segment of <see cref="ConnectionString"/>.
        /// A connection string without it is invalid: reading this property throws.
        /// </summary>
        public string DbName { get; }

        public string DbOperationsCollectionName { get; }

        /// <summary>
        /// True to save the changed models of <see cref="IDbContext.SaveChangesAsync"/> into an
        /// implicit transaction, when the connected deployment supports transactions (replica
        /// set, or sharded cluster). The support is detected at runtime from the cluster
        /// topology: with unsupporting deployments, like standalone servers, saves stay plain.
        /// Set false to disable implicit transactions in any case.
        /// </summary>
        public bool EnableTransactionsWithReplicaSet { get; }

        /// <summary>
        /// How long the exclusive access window (seeding and migrations) waits for the
        /// operations admitted before it opened, still running against the collections of the
        /// db context, before starting its work. A drain unable to complete within the timeout
        /// denies the exclusive work throwing <see cref="TimeoutException"/>: only an operation
        /// running for the whole timeout leaves it in flight.
        /// </summary>
        public TimeSpan ExclusiveAccessDrainTimeout { get; }

        public string? Identifier { get; }

        /// <summary>
        /// How the db context reacts to implicit lazy loads of summary model members:
        /// load logging a warning once per member per scope (the default), load silently, or
        /// deny them throwing <see cref="Exceptions.ScriniumLazyLoadingException"/>.
        /// Preload members explicitly with <see cref="IDbContext.LoadValuesAsync{TModel}(TModel, System.Linq.Expressions.Expression{System.Func{TModel, object?}}[])"/>.
        /// </summary>
        public ReactionMode ImplicitLazyLoad { get; }

        /// <summary>
        /// True to deny any write on the database from this db context: document writes,
        /// index management, seeding and migrations. Reads work normally. Useful to consume
        /// a database owned by another application, avoiding any possibility to write on it.
        /// </summary>
        public bool IsReadOnly { get; }

        /// <summary>
        /// Documents processed by a migration scan between two progress reports on its
        /// operation log. Reporting more often costs a write of the operation document each
        /// time, and tells nothing more about the outcome.
        /// </summary>
        public int MigrationCallbackEveryTotDocuments { get; }

        /// <summary>
        /// Documents processed by a migration scan between two evictions of the models it
        /// loaded and tracked. It bounds the scan memory to the documents of an interval, with
        /// their referenced summaries: a shorter interval holds less memory, a longer one
        /// evicts less often and lets the documents of an interval share what they load.
        /// </summary>
        public int MigrationEvictEveryTotDocuments { get; }

        /// <summary>
        /// How the engine build reacts to not propagated reference paths — reference id
        /// element paths with an unknown document key (a dictionary in document
        /// representation), that the dependencies propagation can't address: report a
        /// warning per element path (the default), tolerate silently, or deny the build
        /// throwing <see cref="Exceptions.ScriniumNotPropagatedReferenceException"/>.
        /// </summary>
        public ReactionMode NotPropagatedReferences { get; }
    }
}