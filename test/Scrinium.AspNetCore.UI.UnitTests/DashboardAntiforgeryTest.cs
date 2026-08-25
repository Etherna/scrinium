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
    public class DashboardAntiforgeryTest
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
        public DashboardAntiforgeryTest()
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
                .ReturnsAsync((DbMigrationOperation?)null);
        }

        // Tests.
        [Fact]
        public async Task IndexPageRendersAntiforgeryToken()
        {
            // Setup.
            using var host = await StartDashboardHostAsync();
            var client = host.GetTestClient();

            // Action.
            var response = await client.GetAsync(new Uri(PagePath, UriKind.Relative));

            // Assert.
            response.EnsureSuccessStatusCode();
            var pageHtml = await response.Content.ReadAsStringAsync();
            Assert.Contains("__RequestVerificationToken", pageHtml, StringComparison.Ordinal);
        }

        [Fact]
        public async Task MigrateDeprecatedSchemaIdDocumentsPostWithoutTokenIsRejected()
        {
            // Setup.
            using var host = await StartDashboardHostAsync();
            var client = host.GetTestClient();

            using var request = new HttpRequestMessage(HttpMethod.Post, PagePath + "?handler=MigrateDeprecatedSchemaIdDocuments");
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["identifier"] = DbContextIdentifier,
                ["repositoryName"] = "posts"
            });

            // Action.
            var response = await client.SendAsync(request);

            // Assert.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task RemoveMissingOriginReferencesPostWithoutTokenIsRejected()
        {
            // Setup.
            using var host = await StartDashboardHostAsync();
            var client = host.GetTestClient();

            using var request = new HttpRequestMessage(HttpMethod.Post, PagePath + "?handler=RemoveMissingOriginReferences");
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["identifier"] = DbContextIdentifier,
                ["repositoryName"] = "posts"
            });

            // Action.
            var response = await client.SendAsync(request);

            // Assert.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task StartMigrationPostWithTokenExecutesHandler()
        {
            // Setup.
            using var host = await StartDashboardHostAsync();
            var client = host.GetTestClient();

            var pageResponse = await client.GetAsync(new Uri(PagePath, UriKind.Relative));
            pageResponse.EnsureSuccessStatusCode();
            var (token, cookie) = await ExtractAntiforgeryAsync(pageResponse);

            using var request = new HttpRequestMessage(HttpMethod.Post, PagePath + "?handler=StartMigration");
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["identifier"] = DbContextIdentifier,
                ["lockLeaseDurationMinutes"] = "10"
            });
            request.Headers.Add("Cookie", cookie);
            //same header sent by scriniumDash.js
            request.Headers.Add("RequestVerificationToken", token);

            // Action.
            var response = await client.SendAsync(request);

            // Assert.
            response.EnsureSuccessStatusCode();
            var responseJson = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"started\":false", responseJson, StringComparison.Ordinal);
            dbContextMock.Verify(
                dbContext => dbContext.TryStartMigrationAsync(false, false, TimeSpan.FromMinutes(10)),
                Times.Once());
        }

        [Fact]
        public async Task StartMigrationPostWithoutTokenIsRejected()
        {
            // Setup.
            using var host = await StartDashboardHostAsync();
            var client = host.GetTestClient();

            using var request = new HttpRequestMessage(HttpMethod.Post, PagePath + "?handler=StartMigration");
            request.Content = new FormUrlEncodedContent(
                new Dictionary<string, string> { ["identifier"] = DbContextIdentifier });

            // Action.
            var response = await client.SendAsync(request);

            // Assert.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            dbContextMock.Verify(
                dbContext => dbContext.TryStartMigrationAsync(
                    It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<TimeSpan?>()),
                Times.Never());
        }

        [Fact]
        public async Task StatusGetRequiresNoToken()
        {
            // Setup.
            using var host = await StartDashboardHostAsync();
            var client = host.GetTestClient();

            // Action.
            var response = await client.GetAsync(new Uri(PagePath + "?handler=Status", UriKind.Relative));

            // Assert.
            response.EnsureSuccessStatusCode();
            var responseJson = await response.Content.ReadAsStringAsync();
            Assert.Contains($"\"identifier\":\"{DbContextIdentifier}\"", responseJson, StringComparison.Ordinal);
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
