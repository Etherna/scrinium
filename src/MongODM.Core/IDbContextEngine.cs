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

using Etherna.MongoDB.Bson.Serialization;
using Etherna.MongoDB.Driver;
using Etherna.MongODM.Core.ExecContext;
using Etherna.MongODM.Core.Options;
using Etherna.MongODM.Core.ProxyModels;
using Etherna.MongODM.Core.Serialization.Mapping;
using Etherna.MongODM.Core.Serialization.Modifiers;
using Etherna.MongODM.Core.Utility;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core
{
    /// <summary>
    /// Interface exposing the scope independent engine of a <see cref="DbContext"/>
    /// implementation: database connections, schema registries, and infrastructure components
    /// built once at context initialization.
    /// </summary>
    public interface IDbContextEngine
    {
        // Properties.
        /// <summary>
        /// Current MongoDB client.
        /// </summary>
        IMongoClient Client { get; }

        /// <summary>
        /// Current MongoDB database.
        /// </summary>
        IMongoDatabase Database { get; }

        /// <summary>
        /// Server side lock of the db context, coordinating its exclusive works (seeding and
        /// migrations) once per db context across every application instance connected to the
        /// database: the resource lock bound to the db context identifier. Applications
        /// configuring different lock collection names for the same database don't exclude
        /// each other.
        /// </summary>
        /// <exception cref="InvalidOperationException">The db context is read-only: claiming
        /// the lock would write on a database it can only read</exception>
        IResourceLock DbContextLock { get; }

        /// <summary>
        /// Type of the db context of this engine.
        /// </summary>
        Type DbContextType { get; }

        /// <summary>
        /// Database operator interested into maintenance tasks.
        /// </summary>
        IDbMaintainer DbMaintainer { get; }

        /// <summary>
        /// Database operator interested into migrations execution.
        /// </summary>
        IDbMigrationManager DbMigrationManager { get; }

        /// <summary>
        /// Registry for discriminator configuration.
        /// </summary>
        IDiscriminatorRegistry DiscriminatorRegistry { get; }

        /// <summary>
        /// ExecutionContext handler.
        /// </summary>
        IExecutionContext ExecutionContext { get; }

        /// <summary>
        /// DbContext unique identifier.
        /// </summary>
        string Identifier { get; }

        /// <summary>
        /// True if an exclusive access operation is running, locking read access for other contexts.
        /// </summary>
        bool IsExclusiveReadEnabled { get; }

        /// <summary>
        /// True if an exclusive access operation is running, locking write access for other contexts.
        /// </summary>
        bool IsExclusiveWriteEnabled { get; }

        /// <summary>
        /// Cached seeding state of the database. Null if still not verified.
        /// Implementations must be thread safe.
        /// </summary>
        bool? IsSeededCache { get; set; }

        /// <summary>
        /// Logger of the db context, available also to the engine level components built
        /// outside the dependency injection container, like serializers.
        /// </summary>
        ILogger Logger { get; }

        /// <summary>
        /// Registry for model serialization and maps information.
        /// </summary>
        IMapRegistry MapRegistry { get; }

        /// <summary>
        /// Db context options.
        /// </summary>
        IDbContextOptions Options { get; }

        /// <summary>
        /// Current model proxy generator.
        /// </summary>
        IProxyGenerator ProxyGenerator { get; }

        /// <summary>
        /// Local instance of a serializer registry.
        /// </summary>
        IBsonSerializerRegistry SerializerRegistry { get; }

        /// <summary>
        /// Serializer modifier accessor.
        /// </summary>
        ISerializerModifierAccessor SerializerModifierAccessor { get; }

        /// <summary>
        /// True when the connected MongoDB deployment supports transactions (replica set, or
        /// sharded cluster), detected from the current cluster topology. False while the
        /// topology is still undiscovered.
        /// </summary>
        bool SupportsTransactions { get; }

        // Methods.
        /// <summary>
        /// Get a collection of the database, guarded by the engine access limitations.
        /// The collection is read-only when required by the parameter, or by the db
        /// context options: any write operation on it, index management included, throws
        /// <see cref="UnauthorizedAccessException"/>.
        /// </summary>
        /// <param name="name">The collection name</param>
        /// <param name="settings">Optional collection settings</param>
        /// <param name="isReadOnly">True to deny writes on the collection</param>
        /// <returns>The guarded collection</returns>
        IMongoCollection<TDocument> GetMongoCollection<TDocument>(
            string name,
            MongoCollectionSettings? settings = null,
            bool isReadOnly = false);

        /// <summary>
        /// Get the resource lock of an application resource, coordinating works on it across
        /// every application instance connected to the database. The resource id lives in
        /// the namespace the application chooses, so locks of different kinds never collide:
        /// the lock identifier is the plain string <c>namespace/resourceId</c>, with the
        /// namespace denied to contain the separator. A db context identifier carrying the
        /// separator could alias an application lock: keep identifiers out of that shape.
        /// The lease documents share the collection of <see cref="DbContextLock"/>, named by
        /// <see cref="IDbContextOptions.DbLockCollectionName"/>.
        /// </summary>
        /// <param name="resourceNamespace">The namespace of the resource, one per lock kind;
        /// it can't contain the <c>/</c> separator</param>
        /// <param name="resourceId">The resource identifier inside its namespace</param>
        /// <returns>The lock of the resource</returns>
        /// <exception cref="ArgumentException">The namespace contains the separator</exception>
        /// <exception cref="InvalidOperationException">The db context is read-only: claiming
        /// a lock would write on a database it can only read</exception>
        IResourceLock GetResourceLock(string resourceNamespace, string resourceId);

        Task RunWithExclusiveAccessAsync(
            Func<Task> action,
            bool lockOnRead = true);

        Task<TResult> RunWithExclusiveAccessAsync<TResult>(
            Func<Task<TResult>> func,
            bool lockOnRead = true);

        /// <summary>
        /// Start a new database transaction session.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The session handler</returns>
        Task<IClientSessionHandle> StartSessionAsync(CancellationToken cancellationToken = default);

    }
}
