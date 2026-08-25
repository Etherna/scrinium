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
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.AspNetCore.UI
{
    public class DashboardAuthorizationTest
    {
        // Internal classes.
        private sealed class ConfigurableAuthFilter(bool isAuthorized) : IDashboardAuthFilter
        {
            public Task<bool> AuthorizeAsync(HttpContext? context) => Task.FromResult(isAuthorized);
        }

        private sealed class TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
        {
            protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            {
                var principal = new ClaimsPrincipal(
                    new ClaimsIdentity([new Claim(ClaimTypes.Name, "dashboardUser")], Scheme.Name));

                return Task.FromResult(AuthenticateResult.Success(
                    new AuthenticationTicket(principal, Scheme.Name)));
            }
        }

        // Consts.
        private const string AuthenticationSchemeName = "DashboardTest";
        private const string DbContextIdentifier = "SampleDbContext";
        private const string PagePath = "/Scrinium";

        // Fields.
        private readonly Mock<IDbContext> dbContextMock;

        // Constructor.
        public DashboardAuthorizationTest()
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
            dbContextMock.Setup(dbContext => dbContext.TryStartMigrationAsync(It.IsAny<bool>(), It.IsAny<bool>()))
                .ReturnsAsync((DbMigrationOperation?)null);
        }

        // Tests.
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public async Task DashboardDeniesWhenAnyFilterDenies(bool firstFilterAuthorizes, bool secondFilterAuthorizes)
        {
            // Setup.
            using var host = await StartDashboardHostAsync(
            [
                new ConfigurableAuthFilter(firstFilterAuthorizes),
                new ConfigurableAuthFilter(secondFilterAuthorizes)
            ]);
            var client = host.GetTestClient();

            // Action.
            var response = await client.GetAsync(new Uri(PagePath, UriKind.Relative));

            // Assert.
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task DashboardGrantsWhenNoFilterIsConfigured()
        {
            /* An application with no authorization of its own leaves the filters empty, instead
             * of configuring a filter allowing everyone: with nothing to deny it, the dashboard
             * is as reachable as the endpoint hosting it. */

            // Setup.
            using var host = await StartDashboardHostAsync([]);
            var client = host.GetTestClient();

            // Action.
            var response = await client.GetAsync(new Uri(PagePath, UriKind.Relative));

            // Assert.
            response.EnsureSuccessStatusCode();
        }

        [Fact]
        public async Task DashboardGrantsWhenEveryFilterAllows()
        {
            // Setup.
            using var host = await StartDashboardHostAsync(
                [new ConfigurableAuthFilter(true), new ConfigurableAuthFilter(true)]);
            var client = host.GetTestClient();

            // Action.
            var response = await client.GetAsync(new Uri(PagePath, UriKind.Relative));

            // Assert.
            response.EnsureSuccessStatusCode();
        }

        [Fact]
        public async Task StartMigrationPostDeniesWhenAFilterDenies()
        {
            // Setup.
            using var host = await StartDashboardHostAsync([new ConfigurableAuthFilter(false)]);
            var client = host.GetTestClient();

            using var request = new HttpRequestMessage(HttpMethod.Post, PagePath + "?handler=StartMigration");
            request.Content = new FormUrlEncodedContent(
                new Dictionary<string, string> { ["identifier"] = DbContextIdentifier });

            // Action.
            var response = await client.SendAsync(request);

            // Assert.
            //the denial comes before the handler: no migration starts
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            dbContextMock.Verify(
                dbContext => dbContext.TryStartMigrationAsync(It.IsAny<bool>(), It.IsAny<bool>()),
                Times.Never());
        }

        // Helpers.
        private async Task<IHost> StartDashboardHostAsync(IEnumerable<IDashboardAuthFilter> authFilters) =>
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
                        /* Authenticate every request: with an authenticated user a denied access
                         * forbids with 403, instead of challenging a scheme the host may not have. */
                        services.AddAuthentication(AuthenticationSchemeName)
                            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                                AuthenticationSchemeName, null);
                        services.AddScriniumAdminDashboard(new DashboardOptions
                        {
                            AuthFilters = authFilters
                        });
                        services.AddSingleton(Options.Create(mongODMOptions));
                        services.AddSingleton(dbContextMock.Object);
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints => endpoints.MapRazorPages());
                    }))
                .StartAsync();
    }
}
