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
using Etherna.MongoDB.Driver.Core.Configuration;
using Etherna.MongODM.Core;
using Etherna.MongODM.Core.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Etherna.MongODM.AspNetCore
{
    public class MongODMConfiguration(IServiceCollection services)
        : IMongODMConfiguration, IDisposable
    {
        // Fields.
        private readonly ReaderWriterLockSlim configLock = new(LockRecursionPolicy.SupportsRecursion);
        private readonly List<Type> dbContextTypes = new();
        private bool disposed;

        // Dispose.
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed) return;

            // Dispose managed resources.
            if (disposing)
                configLock.Dispose();

            disposed = true;
        }

        // Properties.
        public bool IsFrozen { get; private set; }

        // Methods.
        public IMongODMConfiguration AddDbContext<TDbContext>(
            Action<DbContextOptions>? dbContextOptionsConfig = null)
            where TDbContext : DbContext, new() =>
            AddDbContext<TDbContext, TDbContext>(dbContextOptionsConfig);

        public IMongODMConfiguration AddDbContext<TDbContext>(
            TDbContext dbContext,
            Action<DbContextOptions>? dbContextOptionsConfig = null)
            where TDbContext : DbContext =>
            AddDbContext<TDbContext, TDbContext>(_ => dbContext, dbContextOptionsConfig);

        public IMongODMConfiguration AddDbContext<TDbContext>(
            Func<IServiceProvider, TDbContext> dbContextCreator,
            Action<DbContextOptions>? dbContextOptionsConfig = null)
            where TDbContext : DbContext =>
            AddDbContext<TDbContext, TDbContext>(dbContextCreator, dbContextOptionsConfig);

        public IMongODMConfiguration AddDbContext<TDbContext, TDbContextImpl>(
            Action<DbContextOptions>? dbContextOptionsConfig = null)
            where TDbContext : class, IDbContext
            where TDbContextImpl : DbContext, TDbContext, new() =>
            AddDbContext<TDbContext, TDbContextImpl>(
                _ => Activator.CreateInstance<TDbContextImpl>(),
                dbContextOptionsConfig);

        public IMongODMConfiguration AddDbContext<TDbContext, TDbContextImpl>(
            TDbContextImpl dbContext,
            Action<DbContextOptions>? dbContextOptionsConfig = null)
            where TDbContext : class, IDbContext
            where TDbContextImpl : DbContext, TDbContext =>
            AddDbContext<TDbContext, TDbContextImpl>(_ => dbContext, dbContextOptionsConfig);

        public IMongODMConfiguration AddDbContext<TDbContext, TDbContextImpl>(
            Func<IServiceProvider, TDbContextImpl> dbContextCreator,
            Action<DbContextOptions>? dbContextOptionsConfig)
            where TDbContext : class, IDbContext
            where TDbContextImpl : DbContext, TDbContext
        {
            configLock.EnterWriteLock();
            try
            {
                if (IsFrozen)
                    throw new InvalidOperationException("Configuration is frozen");

                // Register dbContext.
                services.AddSingleton(sp =>
                {
                    // Get dependencies.
                    var dependencies = sp.GetRequiredService<IDbDependencies>();
                    var options = new DbContextOptions();
                    dbContextOptionsConfig?.Invoke(options);

                    // Get dbcontext.
                    var dbContext = dbContextCreator(sp);

                    // Initialize instance.
                    var mongoClientSettings = MongoClientSettings.FromConnectionString(options.ConnectionString);
                    mongoClientSettings.ClusterConfigurator = cb =>
                    {
                        var loggerFactory = sp.GetService<ILoggerFactory>();
                        cb.ConfigureLoggingSettings(_ => new LoggingSettings(loggerFactory));
                    };

                    dbContext.Initialize(
                        dependencies,
                        new MongoClient(mongoClientSettings),
                        options,
                        options.ChildDbContextTypes.Select(dbContextType => (IDbContext)sp.GetRequiredService(dbContextType)));

                    return dbContext;
                });
                services.AddSingleton<TDbContext, TDbContextImpl>(sp => sp.GetRequiredService<TDbContextImpl>());

                // Add db context type.
                dbContextTypes.Add(typeof(TDbContext));

                return this;
            }
            finally
            {
                configLock.ExitWriteLock();
            }
        }

        public void Freeze(IMongODMOptionsBuilder mongODMOptionsBuilder)
        {
            ArgumentNullException.ThrowIfNull(mongODMOptionsBuilder, nameof(mongODMOptionsBuilder));

            configLock.EnterReadLock();
            try
            {
                if (IsFrozen) return;
            }
            finally
            {
                configLock.ExitReadLock();
            }

            configLock.EnterWriteLock();
            try
            {
                // Freeze.
                IsFrozen = true;

                // Report configuration to options.
                mongODMOptionsBuilder.SetDbContextTypes(dbContextTypes);
            }
            finally
            {
                configLock.ExitWriteLock();
            }
        }
    }
}
