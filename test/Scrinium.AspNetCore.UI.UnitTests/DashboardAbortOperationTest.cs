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
using Etherna.Scrinium.Core.Utility;
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
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.AspNetCore.UI
{
    /* The abort of the operation running on a db context. The request doesn't have to come
     * from the control the page renders, so the handler verifies the operation is the open one
     * of the db context, with its lock lease alive, before recording the request. */
    public class DashboardAbortOperationTest
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
        private readonly Mock<IResourceLock> dbContextLockMock = new();
        private readonly Mock<IDbContext> dbContextMock;
        private readonly Mock<IDbContextEngine> engineMock;

        // Constructor.
        public DashboardAbortOperationTest()
        {
            engineMock = new Mock<IDbContextEngine>();
            engineMock.Setup(engine => engine.DbContextLock).Returns(dbContextLockMock.Object);
            engineMock.Setup(engine => engine.Identifier).Returns(DbContextIdentifier);
            engineMock.Setup(engine => engine.MapRegistry.MapsByModelType).Returns(new Dictionary<Type, IMap>());
            engineMock.Setup(engine => engine.Options).Returns(new DbContextOptions());

            var repositoryRegistryMock = new Mock<IRepositoryRegistry>();
            repositoryRegistryMock.Setup(registry => registry.Repositories).Returns([]);

            //the lease of the running operation is alive, unless a test says otherwise
            dbContextLockMock.Setup(dbContextLock => dbContextLock.IsLockedAsync())
                .ReturnsAsync(true);

            dbContextMock = new Mock<IDbContext>();
            dbContextMock.Setup(dbContext => dbContext.Engine).Returns(engineMock.Object);
            dbContextMock.Setup(dbContext => dbContext.GetLastOperationsAsync(It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync([]);
            dbContextMock.Setup(dbContext => dbContext.GetLastReferencesRepairsAsync(It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync([]);
            dbContextMock.Setup(dbContext => dbContext.IsMigrationRunningAsync())
                .ReturnsAsync((DbMigrationOperation?)null);
            dbContextMock.Setup(dbContext => dbContext.IsReferencesRepairRunningAsync())
                .ReturnsAsync((ReferencesRepairOperation?)null);
            dbContextMock.Setup(dbContext => dbContext.RepositoryRegistry).Returns(repositoryRegistryMock.Object);
        }

        // Tests.
        [Fact]
        public async Task AbortRecordsTheRequestOnTheRunningMigration()
        {
            // Setup.
            dbContextMock.Setup(dbContext => dbContext.IsMigrationRunningAsync())
                .ReturnsAsync(NewRunningMigration("op-1"));
            dbContextMock.Setup(dbContext => dbContext.TryAbortMigrationAsync("op-1"))
                .ReturnsAsync(true);
            using var host = await StartDashboardHostAsync();

            // Action.
            var response = await PostAbortAsync(host, "op-1");

            // Assert.
            response.EnsureSuccessStatusCode();
            Assert.Contains(
                "\"requested\":true",
                await response.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
            dbContextMock.Verify(dbContext => dbContext.TryAbortMigrationAsync("op-1"), Times.Once());
        }

        [Fact]
        public async Task AbortRecordsTheRequestOnTheRunningReferencesRepair()
        {
            /* The two kinds share the running block, the lifecycle and the lock: the abort
             * reaches the repair through its own request. */

            // Setup.
            dbContextMock.Setup(dbContext => dbContext.IsReferencesRepairRunningAsync())
                .ReturnsAsync(NewRunningRepair("repair-1"));
            dbContextMock.Setup(dbContext => dbContext.TryAbortReferencesRepairAsync("repair-1"))
                .ReturnsAsync(true);
            using var host = await StartDashboardHostAsync();

            // Action.
            var response = await PostAbortAsync(host, "repair-1");

            // Assert.
            response.EnsureSuccessStatusCode();
            Assert.Contains(
                "\"requested\":true",
                await response.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
            dbContextMock.Verify(dbContext => dbContext.TryAbortReferencesRepairAsync("repair-1"), Times.Once());
            dbContextMock.Verify(dbContext => dbContext.TryAbortMigrationAsync(It.IsAny<string>()), Times.Never());
        }

        [Fact]
        public async Task AbortRefusesAnOperationOtherThanTheRunningOne()
        {
            // Setup.
            dbContextMock.Setup(dbContext => dbContext.IsMigrationRunningAsync())
                .ReturnsAsync(NewRunningMigration("op-1"));
            using var host = await StartDashboardHostAsync();

            // Action.
            var response = await PostAbortAsync(host, "op-2");

            // Assert.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(
                "\"requested\":false",
                await response.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
            VerifyNoAbortRequested();
        }

        [Fact]
        public async Task AbortRefusesTheOperationOfADeadOwner()
        {
            /* An open operation whose lease expired is orphaned, not running: nothing executes
             * it anymore, so nothing could observe the request. The next start closes it. */

            // Setup.
            dbContextMock.Setup(dbContext => dbContext.IsMigrationRunningAsync())
                .ReturnsAsync(NewRunningMigration("op-1"));
            dbContextLockMock.Setup(dbContextLock => dbContextLock.IsLockedAsync())
                .ReturnsAsync(false);
            using var host = await StartDashboardHostAsync();

            // Action.
            var response = await PostAbortAsync(host, "op-1");

            // Assert.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            VerifyNoAbortRequested();
        }

        // Helpers.
        private static async Task<(string Token, string Cookie)> ExtractAntiforgeryAsync(HttpResponseMessage pageResponse)
        {
            var pageHtml = await pageResponse.Content.ReadAsStringAsync();
            var tokenMatch = Regex.Match(pageHtml, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
            Assert.True(tokenMatch.Success, "The page doesn't render the antiforgery token");

            var cookie = pageResponse.Headers.GetValues("Set-Cookie")
                .Single(value => value.StartsWith(".AspNetCore.Antiforgery.", StringComparison.Ordinal))
                .Split(';')[0];

            return (tokenMatch.Groups[1].Value, cookie);
        }

        /* The handler verifies the request against the id of the open operation: the mocks
         * hand out operations carrying one, like the stored ones do. */
        private DbMigrationOperation NewRunningMigration(string id)
        {
            var operationMock = new Mock<DbMigrationOperation>(engineMock.Object, false, false, false) { CallBase = true };
            operationMock.Setup(operation => operation.Id).Returns(id);
            operationMock.Object.TaskStarted("task");
            return operationMock.Object;
        }

        private ReferencesRepairOperation NewRunningRepair(string id)
        {
            var operationMock = new Mock<ReferencesRepairOperation>(
                engineMock.Object,
                "posts",
                new Dictionary<string, OriginDeleteMode> { ["Author"] = OriginDeleteMode.RemoveReference },
                false)
            { CallBase = true };
            operationMock.Setup(operation => operation.Id).Returns(id);
            operationMock.Object.TaskStarted("task");
            return operationMock.Object;
        }

        private static async Task<HttpResponseMessage> PostAbortAsync(IHost host, string operationId)
        {
            var client = host.GetTestClient();

            var pageResponse = await client.GetAsync(new Uri(PagePath, UriKind.Relative));
            pageResponse.EnsureSuccessStatusCode();
            var (token, cookie) = await ExtractAntiforgeryAsync(pageResponse);

            using var request = new HttpRequestMessage(HttpMethod.Post, PagePath + "?handler=AbortOperation")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["identifier"] = DbContextIdentifier,
                    ["operationId"] = operationId
                })
            };
            request.Headers.Add("Cookie", cookie);
            //same header sent by scriniumDash.js
            request.Headers.Add("RequestVerificationToken", token);

            return await client.SendAsync(request);
        }

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

        private void VerifyNoAbortRequested()
        {
            dbContextMock.Verify(dbContext => dbContext.TryAbortMigrationAsync(It.IsAny<string>()), Times.Never());
            dbContextMock.Verify(dbContext => dbContext.TryAbortReferencesRepairAsync(It.IsAny<string>()), Times.Never());
        }
    }
}
