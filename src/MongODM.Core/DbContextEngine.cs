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

using Etherna.MongoDB.Bson;
using Etherna.MongoDB.Bson.Serialization;
using Etherna.MongoDB.Driver;
using Etherna.MongoDB.Driver.Core.Clusters;
using Etherna.MongODM.Core.Domain.ModelMaps;
using Etherna.MongODM.Core.ExecContext;
using Etherna.MongODM.Core.Extensions;
using Etherna.MongODM.Core.Options;
using Etherna.MongODM.Core.ProxyModels;
using Etherna.MongODM.Core.Serialization;
using Etherna.MongODM.Core.Serialization.Mapping;
using Etherna.MongODM.Core.Serialization.Modifiers;
using Etherna.MongODM.Core.Serialization.Providers;
using Etherna.MongODM.Core.Utility;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core
{
    /// <summary>
    /// The scope independent engine of a <see cref="DbContext"/>: database connections,
    /// schema registries, and infrastructure components built once at context initialization.
    /// </summary>
    public sealed class DbContextEngine(ILogger logger)
        : IDbContextEngine, IDisposable
    {
        // Fields.
        private IDbContextLock? _dbContextLock;
        private volatile bool _isExclusiveReadEnabled;
        private volatile bool _isExclusiveWriteEnabled;
        private bool? _isSeededCache;
        private BsonSerializerRegistry _serializerRegistry = null!;
        private bool disposed;
        private readonly SemaphoreSlim exclusiveAccessSemaphore = new(1, 1); //support async/await
        private bool isInitialized;
        private readonly ReaderWriterLockSlim isSeededCacheLock = new(); //support read/write locks

        // Initializer.
        public void Initialize(
            IDbDependencies dependencies,
            IMongoClient mongoClient,
            IDbContextOptions options,
            Type dbContextType,
            IEnumerable<IModelMapsCollector> modelMapsCollectors)
        {
            if (isInitialized)
                throw new InvalidOperationException("DbContext engine already initialized");
            ArgumentNullException.ThrowIfNull(dependencies);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(dbContextType);
            ArgumentNullException.ThrowIfNull(modelMapsCollectors);

            // Set dependencies.
            DbContextType = dbContextType;
            DbMaintainer = dependencies.DbMaintainer;
            DbMigrationManager = dependencies.DbMigrationManager;
            DiscriminatorRegistry = dependencies.DiscriminatorRegistry;
            ExecutionContext = dependencies.ExecutionContext;
            MapRegistry = dependencies.MapRegistry;
            Options = options;
            ProxyGenerator = dependencies.ProxyGenerator;
            SerializerModifierAccessor = dependencies.SerializerModifierAccessor;
            _serializerRegistry = (BsonSerializerRegistry)dependencies.BsonSerializerRegistry;

            // Execute initialization into execution context.
            /* Engine level work like schema registration carries the engine on the execution
             * context, with no db context scope. */
            using var dbExecutionContext = new DbExecutionContextHandler(this);

            // Initialize internal dependencies.
            DbMaintainer.Initialize(this, logger);
            DbMigrationManager.Initialize(this, logger);
            DiscriminatorRegistry.Initialize(this, logger);
            MapRegistry.Initialize(this, logger);
            InitializeSerializerRegistry();

            // Register model maps.
            /* Maps registration builds the class maps of the db context: the conventions that
             * MongODM registers on the driver global convention registry apply only inside this
             * scope, leaving every other type automapped in the process to the driver defaults. */
            using (new MapsRegistrationHandler(ExecutionContext))
            {
                //internal maps
                new DbMigrationOperationMap().Register(this);
                new ModelBaseMap().Register(this);
                new OperationBaseMap().Register(this);
                new SeedOperationMap().Register(this);

                //application maps
                foreach (var maps in modelMapsCollectors)
                    maps.Register(this);

                // Build and freeze map registry.
                MapRegistry.Freeze();
            }

            // Initialize MongoDB database.
            Client = mongoClient;
            Database = Client.GetDatabase(options.DbName, new MongoDatabaseSettings
            {
                SerializerRegistry = _serializerRegistry
            });

            // Set as initialized.
            isInitialized = true;

            logger.DbContextInitialized(options.DbName);
        }

        // Dispose.
        public void Dispose()
        {
            if (disposed) return;

            // Dispose managed resources.
            exclusiveAccessSemaphore.Dispose();
            isSeededCacheLock.Dispose();

            disposed = true;
        }

        // Properties.
        public IMongoClient Client { get; private set; } = null!;
        public IMongoDatabase Database { get; private set; } = null!;
        /* Built lazily at first use: the lock collection is accessed raw, out of the engine
         * access limitations, being the coordination infrastructure of the exclusive works. */
        public IDbContextLock DbContextLock
        {
            get
            {
                /* The lock collection is written raw, out of the read-only enforcement of the
                 * guarded collections: deny the whole lock on a read-only db context, or
                 * claiming it would write on a database this db context can only read. */
                if (Options.IsReadOnly)
                    throw new InvalidOperationException(
                        $"Can't access the db context lock of the read-only db context {Identifier}: " +
                        $"claiming it would write the {Options.DbLockCollectionName} collection of database {Options.DbName}. " +
                        "Seeding and migrations of that database belong to the application owning it");

                return LazyInitializer.EnsureInitialized(ref _dbContextLock, () => new DbContextLock(
                    Database.GetCollection<BsonDocument>(Options.DbLockCollectionName),
                    Identifier,
                    ExecutionContext,
                    logger));
            }
        }
        public Type DbContextType { get; private set; } = null!;
        public IDbMaintainer DbMaintainer { get; private set; } = null!;
        public IDbMigrationManager DbMigrationManager { get; private set; } = null!;
        public IDiscriminatorRegistry DiscriminatorRegistry { get; private set; } = null!;
        public IExecutionContext ExecutionContext { get; private set; } = null!;
        public string Identifier => Options.Identifier ?? DbContextType.Name;
        public bool IsExclusiveReadEnabled
        {
            get => _isExclusiveReadEnabled;
            private set => _isExclusiveReadEnabled = value;
        }
        public bool IsExclusiveWriteEnabled
        {
            get => _isExclusiveWriteEnabled;
            private set => _isExclusiveWriteEnabled = value;
        }
        public bool? IsSeededCache
        {
            get
            {
                isSeededCacheLock.EnterReadLock();
                try
                {
                    return _isSeededCache;
                }
                finally
                {
                    isSeededCacheLock.ExitReadLock();
                }
            }
            set
            {
                isSeededCacheLock.EnterWriteLock();
                try
                {
                    _isSeededCache = value;
                }
                finally
                {
                    isSeededCacheLock.ExitWriteLock();
                }
            }
        }
        public ILogger Logger => logger;
        public IMapRegistry MapRegistry { get; private set; } = null!;
        public IDbContextOptions Options { get; private set; } = null!;
        public IProxyGenerator ProxyGenerator { get; private set; } = null!;
        public IBsonSerializerRegistry SerializerRegistry => _serializerRegistry;
        public ISerializerModifierAccessor SerializerModifierAccessor { get; private set; } = null!;
        public bool SupportsTransactions =>
            Client.Cluster.Description.Type is ClusterType.ReplicaSet or ClusterType.Sharded or ClusterType.LoadBalanced;

        // Methods.
        public IMongoCollection<TDocument> GetMongoCollection<TDocument>(
            string name,
            MongoCollectionSettings? settings = null,
            bool isReadOnly = false)
        {
            var mongoCollection = Database.GetCollection<TDocument>(name, settings);
            return new LimitedAccessMongoCollection<TDocument>(this, mongoCollection, isReadOnly || Options.IsReadOnly);
        }

        public Task RunWithExclusiveAccessAsync(
            Func<Task> action,
            bool lockOnRead = true) =>
            RunWithExclusiveAccessAsync(async () =>
            {
                await action().ConfigureAwait(false);
                return 0;
            }, lockOnRead);

        public async Task<TResult> RunWithExclusiveAccessAsync<TResult>(
            Func<Task<TResult>> func,
            bool lockOnRead = true)
        {
            ArgumentNullException.ThrowIfNull(func);

            await exclusiveAccessSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                IsExclusiveReadEnabled = lockOnRead;
                IsExclusiveWriteEnabled = true;

                using var _ = new ExclusiveAccessHandler(this);
                return await func().ConfigureAwait(false);
            }
            finally
            {
                IsExclusiveWriteEnabled = false;
                IsExclusiveReadEnabled = false;

                exclusiveAccessSemaphore.Release();
            }
        }

        public Task<IClientSessionHandle> StartSessionAsync(CancellationToken cancellationToken = default) =>
            Client.StartSessionAsync(cancellationToken: cancellationToken);

        // Helpers.
        private void InitializeSerializerRegistry()
        {
            //order matters. It's in reverse order of how they'll get consumed
            _serializerRegistry.RegisterSerializationProvider(new MapRegistrySerializationProvider(this));
            _serializerRegistry.RegisterSerializationProvider(new DiscriminatedInterfaceSerializationProvider());
            _serializerRegistry.RegisterSerializationProvider(new CollectionsSerializationProvider());
            _serializerRegistry.RegisterSerializationProvider(new PrimitiveSerializationProvider());
            _serializerRegistry.RegisterSerializationProvider(new AttributedSerializationProvider());
            _serializerRegistry.RegisterSerializationProvider(new TypeMappingSerializationProvider());
            _serializerRegistry.RegisterSerializationProvider(new BsonObjectModelSerializationProvider());
        }
    }
}
