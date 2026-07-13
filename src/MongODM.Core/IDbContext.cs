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
using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.ExecContext;
using Etherna.MongODM.Core.Migration;
using Etherna.MongODM.Core.Options;
using Etherna.MongODM.Core.ProxyModels;
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.Core.Serialization.Mapping;
using Etherna.MongODM.Core.Serialization.Modifiers;
using Etherna.MongODM.Core.Utility;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core
{
    /// <summary>
    /// Interface of <see cref="DbContext"/> implementation.
    /// </summary>
    public interface IDbContext
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
        /// Database cache container.
        /// </summary>
        IDbCache DbCache { get; }

        /// <summary>
        /// Database operator interested into maintenance tasks.
        /// </summary>
        IDbMaintainer DbMaintainer { get; }

        /// <summary>
        /// Database operator interested into migrations execution.
        /// </summary>
        IDbMigrationManager DbMigrationManager { get; }

        /// <summary>
        /// Internal collection for keep db operations execution log
        /// </summary>
        IRepository<OperationBase, string> DbOperations { get; }

        /// <summary>
        /// Registry for discriminator configuration.
        /// </summary>
        IDiscriminatorRegistry DiscriminatorRegistry { get; }

        /// <summary>
        /// List of registered migration tasks
        /// </summary>
        IEnumerable<DocumentMigration> DocumentMigrationList { get; }

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
        /// True if it has been seeded.
        /// </summary>
        bool IsSeeded { get; }

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
        /// Registry of available repositories.
        /// </summary>
        IRepositoryRegistry RepositoryRegistry { get; }

        /// <summary>
        /// Local instance of a serializer registry.
        /// </summary>
        IBsonSerializerRegistry SerializerRegistry { get; }

        /// <summary>
        /// Serializer modifier accessor.
        /// </summary>
        ISerializerModifierAccessor SerializerModifierAccessor { get; }

        /// <summary>
        /// ExecutionContext handler.
        /// </summary>
        IExecutionContext ExecutionContext { get; }

        // Methods.
        IMongoCollection<TDocument> GetMongoCollection<TDocument>(string name, MongoCollectionSettings? settings = null);
        
        Task RunWithExclusiveAccessAsync(
            Func<Task> action,
            bool lockOnRead = true);

        Task<TResult> RunWithExclusiveAccessAsync<TResult>(
            Func<Task<TResult>> func,
            bool lockOnRead = true);
        
        /// <summary>
        /// Save current model changes on db.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        Task SaveChangesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Seed database context if still not seeded, applying a db migration before the seed
        /// </summary>
        /// <returns>True if seed has been executed. False otherwise</returns>
        Task<bool> SeedIfNeededAsync();
        
        /// <summary>
        /// Start a new database transaction session.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The session handler</returns>
        Task<IClientSessionHandle> StartSessionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Try to start a db context migration process, if no other migration is queued or running.
        /// </summary>
        /// <returns>The new migration operation, or null if another one is already in progress</returns>
        Task<DbMigrationOperation?> TryStartMigrationAsync();
    }
}