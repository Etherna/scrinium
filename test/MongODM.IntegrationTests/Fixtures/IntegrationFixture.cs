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

using Etherna.MongODM.AspNetCore.Extensions;
using Etherna.MongODM.Core.ExecContext.AsyncLocal;
using Etherna.MongODM.Core.Tasks;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.MongODM.IntegrationTests.Fixtures
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
        public IImplicitSourceDbContext ImplicitSourceDbContext { get; private set; } = null!;
        public string ImplicitSourceDbName { get; } = "mongodm-it-implicit-" + Guid.NewGuid().ToString("N");
        public string MongoDbUrl => mongoDb.DbUrl;
        public string ParentDbName { get; } = "mongodm-it-parent-" + Guid.NewGuid().ToString("N");
        public ISecondDbContext SecondDbContext { get; private set; } = null!;
        public string SecondDbName { get; } = "mongodm-it-second-" + Guid.NewGuid().ToString("N");
        public IServiceProvider ServiceProvider => serviceProvider;
        internal InlineTaskRunner TaskRunner { get; private set; } = null!;
        public ITestDbContext TestDbContext { get; private set; } = null!;
        public string TestDbName { get; } = "mongodm-it-test-" + Guid.NewGuid().ToString("N");

        // Methods.
        public async Task DisposeAsync()
        {
            if (TestDbContext is not null)
            {
                await TestDbContext.Engine.Client.DropDatabaseAsync(ImplicitSourceDbName);
                await TestDbContext.Engine.Client.DropDatabaseAsync(ParentDbName);
                await TestDbContext.Engine.Client.DropDatabaseAsync(TestDbName);
                await TestDbContext.Engine.Client.DropDatabaseAsync(SecondDbName);
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
                .AddDbContext<IImplicitSourceDbContext, ImplicitSourceDbContext>(
                    _ => new ImplicitSourceDbContext(),
                    options =>
                    {
                        options.ConnectionString = $"{mongoDb.DbUrl}/{ImplicitSourceDbName}";
                    });

            serviceProvider = services.BuildServiceProvider();

            ImplicitSourceDbContext = serviceProvider.GetRequiredService<IImplicitSourceDbContext>();
            TaskRunner = (InlineTaskRunner)serviceProvider.GetRequiredService<ITaskRunner>();
            TestDbContext = serviceProvider.GetRequiredService<ITestDbContext>();
            SecondDbContext = serviceProvider.GetRequiredService<ISecondDbContext>();

            // Exercise the seeding path, like at application startup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await TestDbContext.SeedIfNeededAsync();
            await SecondDbContext.SeedIfNeededAsync();
        }
    }

    [CollectionDefinition("Integration")]
    public class IntegrationCollection : ICollectionFixture<IntegrationFixture>
    { }
}
