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
        /// Tracker of models loaded in the current execution scope.
        /// </summary>
        ILoadedModelsTracker LoadedModelsTracker { get; }

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

        // Methods.
        IMongoCollection<TDocument> GetMongoCollection<TDocument>(string name, MongoCollectionSettings? settings = null);

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
