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

using Etherna.Scrinium.AspNetCore.Extensions;
using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Etherna.Scrinium.Core.Tasks;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.IntegrationTests.Fixtures
{
    /// <summary>
    /// Bootstraps the MongODM stack against a real MongoDB instance, mirroring the
    /// production configuration: scoped db contexts over singleton engines, per-flow
    /// async local contexts.
    /// </summary>
    public sealed class IntegrationFixture : IAsyncLifetime
    {
        // Fields.
        private readonly MongoDbFixture mongoDb = new();
        private ServiceProvider serviceProvider = null!;

        // Properties.
        public ICustomIdDbContext CustomIdDbContext { get; private set; } = null!;
        public string CustomIdDbName { get; } = "mongodm-it-customid-" + Guid.NewGuid().ToString("N");
        public IImplicitSourceDbContext ImplicitSourceDbContext { get; private set; } = null!;
        public string ImplicitSourceDbName { get; } = "mongodm-it-implicit-" + Guid.NewGuid().ToString("N");
        public string MigrationsDbName { get; } = "mongodm-it-migrations-" + Guid.NewGuid().ToString("N");
        public LogEventCollector MigrationsLogEvents { get; } = new();
        public IMixedAccessDbContext MixedAccessDbContext { get; private set; } = null!;
        public string MongoDbUrl => mongoDb.DbUrl;
        public IObjectMembersDbContext ObjectMembersDbContext { get; private set; } = null!;
        public string ObjectMembersDbName { get; } = "mongodm-it-objmembers-" + Guid.NewGuid().ToString("N");
        public string ParentDbName { get; } = "mongodm-it-parent-" + Guid.NewGuid().ToString("N");
        public IReadOnlyDbContext ReadOnlyDbContext { get; private set; } = null!;
        public ISecondDbContext SecondDbContext { get; private set; } = null!;
        public string SecondDbName { get; } = "mongodm-it-second-" + Guid.NewGuid().ToString("N");
        public string SeedObserverOneDbName { get; } = "mongodm-it-seedobs1-" + Guid.NewGuid().ToString("N");
        public string SeedObserverTwoDbName { get; } = "mongodm-it-seedobs2-" + Guid.NewGuid().ToString("N");
        public ConcurrentDictionary<string, IDictionary<object, object?>?> SeedingObservations { get; } = new();
        public IServiceProvider ServiceProvider => serviceProvider;
        internal InlineTaskRunner TaskRunner { get; private set; } = null!;
        public ITestDbContext TestDbContext { get; private set; } = null!;
        public string TestDbName { get; } = "mongodm-it-test-" + Guid.NewGuid().ToString("N");

        // Methods.
        public async Task DisposeAsync()
        {
            if (TestDbContext is not null)
            {
                await TestDbContext.Engine.Client.DropDatabaseAsync(CustomIdDbName);
                await TestDbContext.Engine.Client.DropDatabaseAsync(ImplicitSourceDbName);
                await TestDbContext.Engine.Client.DropDatabaseAsync(MigrationsDbName);
                await TestDbContext.Engine.Client.DropDatabaseAsync(ObjectMembersDbName);
                await TestDbContext.Engine.Client.DropDatabaseAsync(ParentDbName);
                await TestDbContext.Engine.Client.DropDatabaseAsync(TestDbName);
                await TestDbContext.Engine.Client.DropDatabaseAsync(SecondDbName);
                await TestDbContext.Engine.Client.DropDatabaseAsync(SeedObserverOneDbName);
                await TestDbContext.Engine.Client.DropDatabaseAsync(SeedObserverTwoDbName);
            }

            if (serviceProvider is not null)
                await serviceProvider.DisposeAsync();

            mongoDb.Dispose();
        }

        public async Task InitializeAsync()
        {
            var services = new ServiceCollection();
            services.AddLogging();

            services.AddMongODM<InlineTaskRunner>()
                .AddDbContext<ITestDbContext, TestDbContext>(
                    _ => new TestDbContext(),
                    options =>
                    {
                        options.ConnectionString = $"{mongoDb.DbUrl}/{TestDbName}";
                    })
                .AddDbContext<ISecondDbContext, SecondDbContext>(
                    _ => new SecondDbContext(),
                    options =>
                    {
                        options.ConnectionString = $"{mongoDb.DbUrl}/{SecondDbName}";
                    })
                .AddDbContext<IParentDbContext, ParentDbContext>(
                    _ => new ParentDbContext(),
                    options =>
                    {
                        options.ConnectionString = $"{mongoDb.DbUrl}/{ParentDbName}";
                        options.ParentFor<ISecondDbContext>();
                    })
                //read-only parent of the second db context, consuming the parent database:
                //the child models propagation must never enqueue toward it
                .AddDbContext<IReadOnlyParentDbContext, ReadOnlyParentDbContext>(
                    _ => new ReadOnlyParentDbContext(),
                    options =>
                    {
                        options.ConnectionString = $"{mongoDb.DbUrl}/{ParentDbName}";
                        options.IsReadOnly = true;
                        options.ParentFor<ISecondDbContext>();
                    })
                .AddDbContext<IImplicitSourceDbContext, ImplicitSourceDbContext>(
                    _ => new ImplicitSourceDbContext(),
                    options =>
                    {
                        options.ConnectionString = $"{mongoDb.DbUrl}/{ImplicitSourceDbName}";
                    })
                //dedicated context for the document migration tests, driving migrations directly
                .AddDbContext<IMigrationsDbContext, MigrationsDbContext>(
                    _ => new MigrationsDbContext(MigrationsLogEvents),
                    options =>
                    {
                        options.ConnectionString = $"{mongoDb.DbUrl}/{MigrationsDbName}";
                    })
                //read-only consumers of the database owned by SecondDbContext
                .AddDbContext<IReadOnlyDbContext, ReadOnlyDbContext>(
                    _ => new ReadOnlyDbContext(),
                    options =>
                    {
                        options.ConnectionString = $"{mongoDb.DbUrl}/{SecondDbName}";
                        options.IsReadOnly = true;
                    })
                .AddDbContext<IMixedAccessDbContext, MixedAccessDbContext>(
                    _ => new MixedAccessDbContext(),
                    options =>
                    {
                        options.ConnectionString = $"{mongoDb.DbUrl}/{SecondDbName}";
                    })
                //dedicated context for the custom serialized entity id tests (MODM-176)
                .AddDbContext<ICustomIdDbContext, CustomIdDbContext>(
                    _ => new CustomIdDbContext(),
                    options =>
                    {
                        options.ConnectionString = $"{mongoDb.DbUrl}/{CustomIdDbName}";
                    })
                //dedicated contexts for the startup seeding tests, left unseeded here
                .AddDbContext<ISeedObserverOneDbContext, SeedObserverOneDbContext>(
                    _ => new SeedObserverOneDbContext(SeedingObservations),
                    options =>
                    {
                        options.ConnectionString = $"{mongoDb.DbUrl}/{SeedObserverOneDbName}";
                    })
                .AddDbContext<ISeedObserverTwoDbContext, SeedObserverTwoDbContext>(
                    _ => new SeedObserverTwoDbContext(SeedingObservations),
                    options =>
                    {
                        options.ConnectionString = $"{mongoDb.DbUrl}/{SeedObserverTwoDbName}";
                    })
                //dedicated context for the object shaped member tests
                .AddDbContext<IObjectMembersDbContext, ObjectMembersDbContext>(
                    _ => new ObjectMembersDbContext(),
                    options =>
                    {
                        options.ConnectionString = $"{mongoDb.DbUrl}/{ObjectMembersDbName}";
                    });

            serviceProvider = services.BuildServiceProvider();

            CustomIdDbContext = serviceProvider.GetRequiredService<ICustomIdDbContext>();
            ImplicitSourceDbContext = serviceProvider.GetRequiredService<IImplicitSourceDbContext>();
            MixedAccessDbContext = serviceProvider.GetRequiredService<IMixedAccessDbContext>();
            ObjectMembersDbContext = serviceProvider.GetRequiredService<IObjectMembersDbContext>();
            ReadOnlyDbContext = serviceProvider.GetRequiredService<IReadOnlyDbContext>();
            TaskRunner = (InlineTaskRunner)serviceProvider.GetRequiredService<ITaskRunner>();
            TestDbContext = serviceProvider.GetRequiredService<ITestDbContext>();
            SecondDbContext = serviceProvider.GetRequiredService<ISecondDbContext>();

            // Exercise the seeding path, like at application startup.
            /* The mixed access db context seeds too: its migration runs the index steps
             * only on its writable repositories. The read-only db context skips seeding. */
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await TestDbContext.SeedIfNeededAsync();
            await SecondDbContext.SeedIfNeededAsync();
            await MixedAccessDbContext.SeedIfNeededAsync();
        }
    }

    [CollectionDefinition("Integration")]
    public class IntegrationCollection : ICollectionFixture<IntegrationFixture>
    { }
}
