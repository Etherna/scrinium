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

using Etherna.MongODM.AspNetCore.UI.Areas.MongODM.Pages;
using Etherna.MongODM.AspNetCore.UI.Auth.Filters;
using Etherna.MongODM.Core;
using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.Domain.Models.DbMigrationOpAgg;
using Etherna.MongODM.Core.Options;
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.Core.Serialization.Mapping;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.MongODM.AspNetCore.UI
{
    public class DashboardHardeningTest
    {
        // Internal classes.
        private sealed class AllowAllAuthFilter : IDashboardAuthFilter
        {
            public Task<bool> AuthorizeAsync(HttpContext? context) => Task.FromResult(true);
        }

        // Consts.
        private const string DbContextIdentifier = "SampleDbContext";
        private const string DocumentId = "5f5f5f5f5f5f5f5f5f5f5f5f";
        /* The exception text of a failing document can quote content of the document itself:
         * a deserialization failure embeds the unrecognized discriminator value. */
        private const string DocumentErrorMessage =
            "MongodmDiscriminatorException: unknown discriminator <script>alert(1)</script>";
        private const string PagePath = "/MongODM";

        // Fields.
        private readonly Mock<IDbContext> dbContextMock;

        // Constructor.
        public DashboardHardeningTest()
        {
            var engineMock = new Mock<IDbContextEngine>();
            engineMock.Setup(engine => engine.Identifier).Returns(DbContextIdentifier);
            engineMock.Setup(engine => engine.MapRegistry.MapsByModelType).Returns(new Dictionary<Type, IMap>());
            engineMock.Setup(engine => engine.Options).Returns(new DbContextOptions());

            var repositoryRegistryMock = new Mock<IRepositoryRegistry>();
            repositoryRegistryMock.Setup(registry => registry.Repositories).Returns([]);

            var documentLog = new DocumentMigrationLog(
                "sampleModels",
                MigrationLogBase.ExecutionState.Failed,
                totMigratedDocs: 3,
                errors: [new DocumentMigrationError(DocumentId, DocumentErrorMessage)],
                totErrorDocs: 1);
            var operationMock = new Mock<DbMigrationOperation>();
            operationMock.Setup(operation => operation.Id).Returns("6f6f6f6f6f6f6f6f6f6f6f6f");
            operationMock.Setup(operation => operation.CurrentStatus).Returns(DbMigrationOperation.Status.Failed);
            operationMock.Setup(operation => operation.Logs).Returns([documentLog]);

            dbContextMock = new Mock<IDbContext>();
            dbContextMock.Setup(dbContext => dbContext.Engine).Returns(engineMock.Object);
            dbContextMock.Setup(dbContext => dbContext.GetLastMigrationsAsync(It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync([operationMock.Object]);
            dbContextMock.Setup(dbContext => dbContext.IsMigrationRunningAsync())
                .ReturnsAsync((DbMigrationOperation?)null);
            dbContextMock.Setup(dbContext => dbContext.RepositoryRegistry).Returns(repositoryRegistryMock.Object);
        }

        // Tests.
        [Theory]
        [InlineData("/", "")]
        [InlineData("/MongODM", "MongODM")]
        [InlineData("MongODM/", "MongODM")]
        [InlineData("admin//mongodm", "admin/mongodm")]
        [InlineData(null, "")]
        [InlineData(" ", "")]
        public async Task DashboardRegistrationNormalizesTheBasePath(string? basePath, string expectedPath)
        {
            /* The base path replaces the area name as first route segment of every dashboard
             * page, so leading, trailing and repeated '/' would render routes carrying an empty
             * segment: every one of those values names the same path, and is served on it. A
             * value left empty mounts the dashboard on the application root, what an application
             * dedicated to it wants. */

            // Setup.
            var dashboardOptions = new DashboardOptions
            {
                AuthFilters = [new AllowAllAuthFilter()],
                BasePath = basePath!
            };
            using var host = await StartDashboardHostAsync(dashboardOptions);
            var client = host.GetTestClient();

            // Action.
            var response = await client.GetAsync(new Uri("/" + expectedPath, UriKind.Relative));

            // Assert.
            response.EnsureSuccessStatusCode();
            Assert.Equal(expectedPath, dashboardOptions.BasePath);
        }

        [Theory]
        [InlineData("MongODM")]
        [InlineData("admin/mongodm")]
        public async Task DashboardRegistrationKeepsServingValidBasePaths(string basePath)
        {
            // Setup.
            using var host = await StartDashboardHostAsync(new DashboardOptions
            {
                AuthFilters = [new AllowAllAuthFilter()],
                BasePath = basePath
            });
            var client = host.GetTestClient();

            // Action.
            var response = await client.GetAsync(new Uri("/" + basePath, UriKind.Relative));

            // Assert.
            response.EnsureSuccessStatusCode();
        }

        [Fact]
        public async Task DocumentErrorMessagesAreEncodedAsData()
        {
            /* The error message of a failing document is the text of the exception that failed
             * it, so it can carry document content and its markup: the json serializer encodes
             * it, and the dashboard script lands it on textContent. Both have to keep holding. */

            // Setup.
            using var host = await StartDashboardHostAsync(new DashboardOptions
            {
                AuthFilters = [new AllowAllAuthFilter()]
            });
            var client = host.GetTestClient();

            // Action.
            var response = await client.GetAsync(new Uri(PagePath + "?handler=Status", UriKind.Relative));

            // Assert.
            response.EnsureSuccessStatusCode();
            var responseJson = await response.Content.ReadAsStringAsync();
            //the failing document is reported with its id and its error
            Assert.Contains($"\"documentId\":\"{DocumentId}\"", responseJson, StringComparison.Ordinal);
            Assert.Contains("unknown discriminator", responseJson, StringComparison.Ordinal);
            Assert.DoesNotContain("<script>", responseJson, StringComparison.Ordinal);
            Assert.Contains("\\u003Cscript\\u003E", responseJson, StringComparison.Ordinal);
        }

        [Fact]
        public async Task JsonHandlersDenyCachingAndContentTypeSniffing()
        {
            // Setup.
            using var host = await StartDashboardHostAsync(new DashboardOptions
            {
                AuthFilters = [new AllowAllAuthFilter()]
            });
            var client = host.GetTestClient();

            // Action.
            var response = await client.GetAsync(new Uri(PagePath + "?handler=Status", UriKind.Relative));

            // Assert.
            response.EnsureSuccessStatusCode();
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        }

        [Fact]
        public async Task PageDeniesFramingAndForeignContentSources()
        {
            // Setup.
            using var host = await StartDashboardHostAsync(new DashboardOptions
            {
                AuthFilters = [new AllowAllAuthFilter()]
            });
            var client = host.GetTestClient();

            // Action.
            var response = await client.GetAsync(new Uri(PagePath, UriKind.Relative));

            // Assert.
            response.EnsureSuccessStatusCode();
            Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
            Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
            var contentSecurityPolicy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
            Assert.Contains("default-src 'none'", contentSecurityPolicy, StringComparison.Ordinal);
            Assert.Contains("frame-ancestors 'none'", contentSecurityPolicy, StringComparison.Ordinal);
            //the self contained assets of the page are the only allowed sources
            Assert.Contains("script-src 'self'", contentSecurityPolicy, StringComparison.Ordinal);
            Assert.Contains("style-src 'self'", contentSecurityPolicy, StringComparison.Ordinal);
        }

        // Helpers.
        private async Task<IHost> StartDashboardHostAsync(DashboardOptions dashboardOptions) =>
            await new HostBuilder()
                .ConfigureWebHost(webHostBuilder => webHostBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        var mongODMOptions = new MongODMOptions();
                        ((IMongODMOptionsBuilder)mongODMOptions).SetDbContextTypes([typeof(IDbContext)]);

                        services.AddRazorPages()
                            .AddApplicationPart(typeof(IndexModel).Assembly);
                        services.AddHttpContextAccessor();
                        services.AddMongODMAdminDashboard(dashboardOptions);
                        services.AddSingleton(Options.Create(mongODMOptions));
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
