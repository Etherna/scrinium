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

using Etherna.Scrinium.AspNetCore.UI.Areas.Scrinium.Pages;
using Etherna.Scrinium.AspNetCore.UI.Auth.Filters;
using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Options;
using Etherna.Scrinium.Core.Repositories;
using Etherna.Scrinium.Core.Serialization.Mapping;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.AspNetCore.UI
{
    /* SCR-288: the polled status carries the latest operations alone, so the whole history is
     * reachable only through its own handler, walked one page at a time. The operations
     * collection is polymorphic, and so is the history: migrations, references repairs and
     * seedings render side by side, each with what it records. */
    public class DashboardOperationsHistoryTest
    {
        // Internal classes.
        private sealed class AllowAllAuthFilter : IDashboardAuthFilter
        {
            public Task<bool> AuthorizeAsync(HttpContext? context) => Task.FromResult(true);
        }

        // Consts.
        private const string DbContextIdentifier = "SampleDbContext";
        private const string PagePath = "/Scrinium";

        // Fields.
        private readonly Mock<IDbContextEngine> engineMock;
        private readonly Mock<IDbContext> dbContextMock;

        // Constructor.
        public DashboardOperationsHistoryTest()
        {
            engineMock = new Mock<IDbContextEngine>();
            engineMock.Setup(engine => engine.Identifier).Returns(DbContextIdentifier);
            engineMock.Setup(engine => engine.MapRegistry.MapsByModelType).Returns(new Dictionary<Type, IMap>());
            engineMock.Setup(engine => engine.Options).Returns(new DbContextOptions());

            var repositoryRegistryMock = new Mock<IRepositoryRegistry>();
            repositoryRegistryMock.Setup(registry => registry.Repositories).Returns([]);

            dbContextMock = new Mock<IDbContext>();
            dbContextMock.Setup(dbContext => dbContext.Engine).Returns(engineMock.Object);
            dbContextMock.Setup(dbContext => dbContext.IsMigrationRunningAsync())
                .ReturnsAsync((DbMigrationOperation?)null);
            dbContextMock.Setup(dbContext => dbContext.IsReferencesRepairRunningAsync())
                .ReturnsAsync((ReferencesRepairOperation?)null);
            dbContextMock.Setup(dbContext => dbContext.GetLastReferencesRepairsAsync(It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync([]);
            dbContextMock.Setup(dbContext => dbContext.RepositoryRegistry).Returns(repositoryRegistryMock.Object);
        }

        // Tests.
        [Fact]
        public async Task HistoryPageAnnouncesNoMoreAfterAPartialPage()
        {
            /* A page shorter than the requested one is the last of the history: nothing
             * older is left to walk to. */

            // Setup.
            SetupHistory(fillPage: false);
            using var host = await StartDashboardHostAsync();

            // Action.
            var response = await GetHistoryPageAsync(host, "0");

            // Assert.
            response.EnsureSuccessStatusCode();
            Assert.Contains(
                "\"hasMore\":false",
                await response.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        [Fact]
        public async Task HistoryPageReadsTheRequestedPageAndAnnouncesTheOlderOnes()
        {
            // Setup.
            SetupHistory(fillPage: true);
            using var host = await StartDashboardHostAsync();

            // Action.
            var response = await GetHistoryPageAsync(host, "2");

            // Assert.
            response.EnsureSuccessStatusCode();
            var responseJson = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"historyPage\":2", responseJson, StringComparison.Ordinal);
            //a full page can be followed by another one
            Assert.Contains("\"hasMore\":true", responseJson, StringComparison.Ordinal);
            dbContextMock.Verify(
                dbContext => dbContext.GetLastOperationsAsync(2, It.IsAny<int>()),
                Times.Once());
        }

        [Fact]
        public async Task HistoryPageRendersEveryKindOfOperation()
        {
            /* Migrations, references repairs and seedings share the operations collection: the
             * history reads them as they are stored, and each reports what it records — a
             * seeding records nothing but its own existence. */

            // Setup.
            var repairOperation = new ReferencesRepairOperation(
                engineMock.Object,
                "posts",
                new Dictionary<string, OriginDeleteMode>
                {
                    ["Author"] = OriginDeleteMode.DeleteReferencingDocument
                });
            dbContextMock.Setup(dbContext => dbContext.GetLastOperationsAsync(It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync([
                    new DbMigrationOperation(engineMock.Object),
                    repairOperation,
                    new SeedOperation(engineMock.Object)
                ]);
            using var host = await StartDashboardHostAsync();

            // Action.
            var response = await GetHistoryPageAsync(host, "0");

            // Assert.
            response.EnsureSuccessStatusCode();
            var responseJson = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"kind\":\"Migration\"", responseJson, StringComparison.Ordinal);
            Assert.Contains("\"kind\":\"Seed\"", responseJson, StringComparison.Ordinal);
            Assert.Contains("\"kind\":\"ReferencesRepair\"", responseJson, StringComparison.Ordinal);
            //a repair carries the collection it repairs, and the plan it was started with
            Assert.Contains("\"repository\":\"posts\"", responseJson, StringComparison.Ordinal);
            Assert.Contains("\"elementPath\":\"Author\"", responseJson, StringComparison.Ordinal);
        }

        [Theory]
        //a negative page, and one whose skipped operations amount doesn't fit an int
        [InlineData("-1")]
        [InlineData("2147483647")]
        public async Task HistoryPageRejectsAnUnusablePage(string historyPage)
        {
            /* The page arrives from the browser: the paged query refuses these as argument
             * errors, so the handler refuses them as the bad requests they are. */

            // Setup.
            SetupHistory(fillPage: false);
            using var host = await StartDashboardHostAsync();

            // Action.
            var response = await GetHistoryPageAsync(host, historyPage);

            // Assert.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            dbContextMock.Verify(
                dbContext => dbContext.GetLastOperationsAsync(It.IsAny<int>(), It.IsAny<int>()),
                Times.Never());
        }

        // Helpers.
        private static async Task<HttpResponseMessage> GetHistoryPageAsync(IHost host, string historyPage) =>
            await host.GetTestClient().GetAsync(new Uri(
                $"{PagePath}?handler=Operations&identifier={DbContextIdentifier}&historyPage={historyPage}",
                UriKind.Relative));

        /* A page as long as the requested one has older operations behind it, a shorter one
         * is the last: the handler tells them apart without knowing the history size. */
        private void SetupHistory(bool fillPage) =>
            dbContextMock.Setup(dbContext => dbContext.GetLastOperationsAsync(It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync((int _, int take) => Enumerable
                    .Range(0, fillPage ? take : take - 1)
                    .Select(_ => (OperationBase)new DbMigrationOperation(engineMock.Object))
                    .ToList());

        private async Task<IHost> StartDashboardHostAsync() =>
            await new HostBuilder()
                .ConfigureWebHost(webHostBuilder => webHostBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        var scriniumOptions = new ScriniumOptions();
                        ((IScriniumOptionsBuilder)scriniumOptions).SetDbContextTypes([typeof(IDbContext)]);

                        services.AddRazorPages()
                            .AddApplicationPart(typeof(IndexModel).Assembly);
                        services.AddHttpContextAccessor();
                        services.AddScriniumAdminDashboard(new DashboardOptions
                        {
                            AuthFilters = [new AllowAllAuthFilter()]
                        });
                        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(scriniumOptions));
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
