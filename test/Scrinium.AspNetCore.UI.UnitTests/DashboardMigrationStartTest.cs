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
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.AspNetCore.UI
{
    /* The lock lease duration of a migration start arrives from the browser: the page controls
     * bound it, but nothing guarantees the request comes from them. */
    public class DashboardMigrationStartTest
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
        private readonly Mock<IDbContext> dbContextMock;

        // Constructor.
        public DashboardMigrationStartTest()
        {
            var engineMock = new Mock<IDbContextEngine>();
            engineMock.Setup(engine => engine.Identifier).Returns(DbContextIdentifier);
            engineMock.Setup(engine => engine.MapRegistry.MapsByModelType).Returns(new Dictionary<Type, IMap>());
            engineMock.Setup(engine => engine.Options).Returns(new DbContextOptions());

            var repositoryRegistryMock = new Mock<IRepositoryRegistry>();
            repositoryRegistryMock.Setup(registry => registry.Repositories).Returns([]);

            dbContextMock = new Mock<IDbContext>();
            dbContextMock.Setup(dbContext => dbContext.Engine).Returns(engineMock.Object);
            dbContextMock.Setup(dbContext => dbContext.GetLastMigrationsAsync(It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync([]);
            dbContextMock.Setup(dbContext => dbContext.IsMigrationRunningAsync())
                .ReturnsAsync((DbMigrationOperation?)null);
            dbContextMock.Setup(dbContext => dbContext.RepositoryRegistry).Returns(repositoryRegistryMock.Object);
            dbContextMock.Setup(dbContext => dbContext.TryStartMigrationAsync(
                    It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<TimeSpan?>()))
                .ReturnsAsync(new DbMigrationOperation(engineMock.Object));
        }

        // Tests.
        [Fact]
        public async Task PageRendersTheLockLeaseDurationControl()
        {
            // Setup.
            using var host = await StartDashboardHostAsync();

            // Action.
            var response = await host.GetTestClient().GetAsync(new Uri(PagePath, UriKind.Relative));

            // Assert.
            response.EnsureSuccessStatusCode();
            var pageHtml = await response.Content.ReadAsStringAsync();
            Assert.Contains("data-role=\"lock-lease-duration\"", pageHtml, StringComparison.Ordinal);
            //the control offers the default lease duration, inside the range the handler accepts
            Assert.Contains(
                $"value=\"{(int)ResourceLock.DefaultLeaseDuration.TotalMinutes}\"",
                pageHtml,
                StringComparison.Ordinal);
            Assert.Contains(
                $"max=\"{IndexModel.MaxLockLeaseDurationMinutes}\"",
                pageHtml,
                StringComparison.Ordinal);
            //what the lease duration is stays reachable, behind the info tip of the control
            Assert.Contains("info-tip", pageHtml, StringComparison.Ordinal);
        }

        [Fact]
        public async Task StartForwardsTheRequestedLockLeaseDuration()
        {
            // Setup.
            using var host = await StartDashboardHostAsync();

            // Action.
            var response = await PostStartMigrationAsync(host, "25");

            // Assert.
            response.EnsureSuccessStatusCode();
            Assert.Contains(
                "\"started\":true",
                await response.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
            dbContextMock.Verify(
                dbContext => dbContext.TryStartMigrationAsync(false, false, TimeSpan.FromMinutes(25)),
                Times.Once());
        }

        [Fact]
        public async Task StartRejectsALockLeaseDurationOverTheMaximum()
        {
            /* An unbounded lease would keep the db context locked for as long as it says, with
             * no way to start a migration or a seeding of it meanwhile. */

            // Setup.
            using var host = await StartDashboardHostAsync();

            // Action.
            var response = await PostStartMigrationAsync(
                host,
                (IndexModel.MaxLockLeaseDurationMinutes + 1).ToString(CultureInfo.InvariantCulture));

            // Assert.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            //the page renders the error of the body through its feedback path
            var responseJson = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"started\":false", responseJson, StringComparison.Ordinal);
            Assert.Contains(
                IndexModel.MaxLockLeaseDurationMinutes.ToString(CultureInfo.InvariantCulture),
                responseJson,
                StringComparison.Ordinal);
            VerifyNoMigrationStarted();
        }

        [Theory]
        //absent: a start without a lease duration has nothing to claim the lock with
        [InlineData(null)]
        [InlineData("0")]
        [InlineData("-5")]
        [InlineData("notANumber")]
        public async Task StartRejectsAnUnusableLockLeaseDuration(string? lockLeaseDurationMinutes)
        {
            // Setup.
            using var host = await StartDashboardHostAsync();

            // Action.
            var response = await PostStartMigrationAsync(host, lockLeaseDurationMinutes);

            // Assert.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var responseJson = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"started\":false", responseJson, StringComparison.Ordinal);
            Assert.Contains("positive", responseJson, StringComparison.Ordinal);
            VerifyNoMigrationStarted();
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

        private static async Task<HttpResponseMessage> PostStartMigrationAsync(
            IHost host,
            string? lockLeaseDurationMinutes)
        {
            var client = host.GetTestClient();

            var pageResponse = await client.GetAsync(new Uri(PagePath, UriKind.Relative));
            pageResponse.EnsureSuccessStatusCode();
            var (token, cookie) = await ExtractAntiforgeryAsync(pageResponse);

            var form = new Dictionary<string, string> { ["identifier"] = DbContextIdentifier };
            if (lockLeaseDurationMinutes is not null)
                form["lockLeaseDurationMinutes"] = lockLeaseDurationMinutes;

            using var request = new HttpRequestMessage(HttpMethod.Post, PagePath + "?handler=StartMigration")
            {
                Content = new FormUrlEncodedContent(form)
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

        private void VerifyNoMigrationStarted() =>
            dbContextMock.Verify(
                dbContext => dbContext.TryStartMigrationAsync(
                    It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<TimeSpan?>()),
                Times.Never());
    }
}
