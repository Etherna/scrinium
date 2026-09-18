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
using Etherna.MongoDB.Driver.Core.Configuration;
using Etherna.Scrinium.AspNetCore.ExecContext;
using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.ExecContext;
using Etherna.Scrinium.Core.Options;
using Etherna.Scrinium.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Etherna.Scrinium.AspNetCore
{
    public class ScriniumConfiguration(IServiceCollection services)
        : IScriniumConfiguration
    {
        // Fields.
        private readonly object configLock = new();
        private readonly List<DbContextRegistration> dbContextRegistrations = [];
        private readonly List<Type> dbContextTypes = new();

        // Properties.
        public bool IsFrozen { get; private set; }

        // Methods.
        public IScriniumConfiguration AddDbContext<TDbContext>(
            Action<DbContextOptions>? dbContextOptionsConfig = null)
            where TDbContext : DbContext, new() =>
            AddDbContext<TDbContext, TDbContext>(dbContextOptionsConfig);

        public IScriniumConfiguration AddDbContext<TDbContext>(
            Func<IServiceProvider, TDbContext> dbContextCreator,
            Action<DbContextOptions>? dbContextOptionsConfig = null)
            where TDbContext : DbContext =>
            AddDbContext<TDbContext, TDbContext>(dbContextCreator, dbContextOptionsConfig);

        public IScriniumConfiguration AddDbContext<TDbContext, TDbContextImpl>(
            Action<DbContextOptions>? dbContextOptionsConfig = null)
            where TDbContext : class, IDbContext
            where TDbContextImpl : DbContext, TDbContext, new() =>
            AddDbContext<TDbContext, TDbContextImpl>(
                _ => Activator.CreateInstance<TDbContextImpl>(),
                dbContextOptionsConfig);

        public IScriniumConfiguration AddDbContext<TDbContext, TDbContextImpl>(
            Func<IServiceProvider, TDbContextImpl> dbContextCreator,
            Action<DbContextOptions>? dbContextOptionsConfig)
            where TDbContext : class, IDbContext
            where TDbContextImpl : DbContext, TDbContext
        {
            lock (configLock)
            {
                if (IsFrozen)
                    throw new InvalidOperationException("Configuration is frozen");

                // Build dbContext options.
                var options = new DbContextOptions();
                dbContextOptionsConfig?.Invoke(options);

                // Deny a children declaration closing a cycle.
                /* A child attaches to the scoped instance of its parent, and seeds before it:
                 * db contexts declaring each other as children, directly or through other
                 * ones, could neither resolve nor seed. A declared child type still not
                 * registered is verified by its own registration. */
                var registration = new DbContextRegistration(typeof(TDbContext), typeof(TDbContextImpl), options);
                var childrenCycle = FindChildrenCycle([registration], [.. dbContextRegistrations, registration]);
                if (childrenCycle is not null)
                    throw new InvalidOperationException(
                        $"Can't register db context {typeof(TDbContext).Name}: the child db contexts declared with " +
                        $"{nameof(DbContextOptions)}.{nameof(DbContextOptions.ParentFor)} close a cycle " +
                        $"({string.Join(" -> ", childrenCycle.Append(registration).Select(r => r.ServiceType.Name))})");

                // Register dbContext engine, keyed by its dbContext type.
                services.AddKeyedSingleton<IDbContextEngine>(typeof(TDbContextImpl), (sp, _) =>
                {
                    // Bind the driver static hooks to the application execution context.
                    /* The hooks resolve the db context engine of the current flow from the
                     * execution context, and are configured with the service collection: the
                     * application service provider is available only here. */
                    DeferredExecutionContext.Instance.Bind(sp.GetRequiredService<IExecutionContext>());

                    // Get dependencies.
                    var dependencies = sp.GetRequiredService<IDbDependencies>();

                    // Build the engine from the definitions of a discardable db context instance.
                    var mongoClientSettings = MongoClientSettings.FromConnectionString(options.ConnectionString);
                    mongoClientSettings.ClusterConfigurator = cb =>
                    {
                        var loggerFactory = sp.GetService<ILoggerFactory>();
                        cb.ConfigureLoggingSettings(_ => new LoggingSettings(loggerFactory));
                    };

                    return dbContextCreator(sp).BuildEngine(
                        dependencies,
                        new MongoClient(mongoClientSettings),
                        options);
                });

                // Register scoped dbContext instances, attached to the singleton engine.
                services.AddScoped(sp =>
                {
                    var engine = sp.GetRequiredKeyedService<IDbContextEngine>(typeof(TDbContextImpl));

                    var dbContext = dbContextCreator(sp);
                    dbContext.AttachToEngine(
                        engine,
                        options.ChildDbContextTypes.Select(dbContextType => (IDbContext)sp.GetRequiredService(dbContextType)).ToArray(),
                        sp.GetRequiredService<IRepositoryRegistry>());

                    return dbContext;
                });
                services.AddScoped<TDbContext, TDbContextImpl>(sp => sp.GetRequiredService<TDbContextImpl>());

                // Record the registration, for the parent engines resolution and the seeding order.
                dbContextRegistrations.Add(registration);
                services.AddSingleton(registration);

                // Add db context type.
                dbContextTypes.Add(typeof(TDbContext));

                return this;
            }
        }

        public void Freeze(IScriniumOptionsBuilder scriniumOptionsBuilder)
        {
            ArgumentNullException.ThrowIfNull(scriniumOptionsBuilder);

            lock (configLock)
            {
                if (IsFrozen) return;

                // Freeze.
                IsFrozen = true;

                // Report configuration to options.
                scriniumOptionsBuilder.SetDbContextTypes(dbContextTypes);
            }
        }

        // Helpers.
        /// <summary>
        /// Find the children declarations leading from the first db context of a path back to
        /// itself, resolving each declared child type to its registration like the scope attach
        /// resolves its instance
        /// </summary>
        private static DbContextRegistration[]? FindChildrenCycle(
            DbContextRegistration[] path,
            DbContextRegistration[] registrations) =>
            path[^1].Options.ChildDbContextTypes
                .Select(childDbContextType => registrations
                    .LastOrDefault(registration =>
                        registration.ServiceType == childDbContextType ||
                        registration.ImplementationType == childDbContextType))
                .OfType<DbContextRegistration>()
                .Select(childRegistration => ReferenceEquals(childRegistration, path[0]) ?
                    path :
                    FindChildrenCycle([.. path, childRegistration], registrations))
                .FirstOrDefault(childrenCycle => childrenCycle is not null);
    }
}
