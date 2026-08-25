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

using Etherna.Scrinium.AspNetCore.UI.Areas.Scrinium.Pages;
using Etherna.Scrinium.AspNetCore.UI.Auth.Filters;
using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Options;
using Etherna.Scrinium.Core.Repositories;
using Etherna.Scrinium.Core.Utility;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.AspNetCore.UI
{
    public class DashboardStatusTest
    {
        // Internal classes.
        private sealed class AllowAllAuthFilter : IDashboardAuthFilter
        {
            public Task<bool> AuthorizeAsync(HttpContext? context) => Task.FromResult(true);
        }

        // Consts.
        private const string DbContextIdentifier = "SampleDbContext";
        private const string StatusPath = "/Scrinium?handler=Status";

        // Fields.
        private readonly Mock<IDbContext> dbContextMock;
        private readonly Mock<IResourceLock> dbContextLockMock = new();

        // Constructor.
        public DashboardStatusTest()
        {
            var engineMock = new Mock<IDbContextEngine>();
            engineMock.Setup(engine => engine.DbContextLock).Returns(dbContextLockMock.Object);
            engineMock.Setup(engine => engine.Identifier).Returns(DbContextIdentifier);
            engineMock.Setup(engine => engine.Options).Returns(new DbContextOptions());

            var repositoryRegistryMock = new Mock<IRepositoryRegistry>();
            repositoryRegistryMock.Setup(registry => registry.Repositories).Returns([]);

            //an operation left open by the instance that was executing it
            var openOperation = new DbMigrationOperation(engineMock.Object);
            openOperation.TaskStarted("dead-task");

            dbContextMock = new Mock<IDbContext>();
            dbContextMock.Setup(dbContext => dbContext.Engine).Returns(engineMock.Object);
            dbContextMock.Setup(dbContext => dbContext.GetLastMigrationsAsync(It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync([openOperation]);
            dbContextMock.Setup(dbContext => dbContext.IsMigrationRunningAsync())
                .ReturnsAsync(openOperation);
            dbContextMock.Setup(dbContext => dbContext.RepositoryRegistry).Returns(repositoryRegistryMock.Object);
        }

        // Tests.
        [Fact]
        public async Task StatusReportsAMigrationRunningWithALiveLockLease()
        {
            // Setup.
            dbContextLockMock.Setup(dbContextLock => dbContextLock.IsLockedAsync())
                .ReturnsAsync(true);
            using var host = await StartDashboardHostAsync();

            // Action.
            var responseJson = await ReadStatusAsync(host);

            // Assert.
            Assert.Contains("\"isLocked\":true", responseJson, StringComparison.Ordinal);
            Assert.Contains("\"runningOperation\":{", responseJson, StringComparison.Ordinal);
        }

        [Fact]
        public async Task StatusReportsNoMigrationRunningWithAnExpiredLockLease()
        {
            /* The instance executing the operation died: its lease expired, and only a new
             * start closes the orphaned operation. Reporting it as running would disable the
             * start controls forever. */

            // Setup.
            dbContextLockMock.Setup(dbContextLock => dbContextLock.IsLockedAsync())
                .ReturnsAsync(false);
            using var host = await StartDashboardHostAsync();

            // Action.
            var responseJson = await ReadStatusAsync(host);

            // Assert.
            Assert.Contains("\"isLocked\":false", responseJson, StringComparison.Ordinal);
            Assert.Contains("\"runningOperation\":null", responseJson, StringComparison.Ordinal);
        }

        // Helpers.
        private static async Task<string> ReadStatusAsync(IHost host)
        {
            var response = await host.GetTestClient().GetAsync(new Uri(StatusPath, UriKind.Relative));

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        private async Task<IHost> StartDashboardHostAsync() =>
            await new HostBuilder()
                .ConfigureWebHost(webHostBuilder => webHostBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        var mongODMOptions = new ScriniumOptions();
                        ((IScriniumOptionsBuilder)mongODMOptions).SetDbContextTypes([typeof(IDbContext)]);

                        services.AddRazorPages()
                            .AddApplicationPart(typeof(IndexModel).Assembly);
                        services.AddHttpContextAccessor();
                        services.AddScriniumAdminDashboard(new DashboardOptions
                        {
                            AuthFilters = [new AllowAllAuthFilter()]
                        });
                        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(mongODMOptions));
                        services.AddSingleton(dbContextMock.Object);
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints => endpoints.MapRazorPages());
                    }))
                .StartAsync();
    }
}
