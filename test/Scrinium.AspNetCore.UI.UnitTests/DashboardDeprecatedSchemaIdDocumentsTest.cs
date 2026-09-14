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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.AspNetCore.UI
{
    /* SCR-256: the dashboard counts the documents carrying their schema id under a deprecated
     * element name and migrates them, one collection at a time, delegating the work to the
     * collection repository. */
    public class DashboardDeprecatedSchemaIdDocumentsTest
    {
        // Internal classes.
        private sealed class AllowAllAuthFilter : IDashboardAuthFilter
        {
            public Task<bool> AuthorizeAsync(HttpContext? context) => Task.FromResult(true);
        }

        // Consts.
        private const string DbContextIdentifier = "SampleDbContext";
        private const string PagePath = "/Scrinium";
        private const string ReadOnlyRepositoryName = "readonlyNotes";
        private const string RepositoryName = "posts";

        // Fields.
        private readonly Mock<IDbContext> dbContextMock;
        private readonly Mock<IRepository> repositoryMock;

        // Constructor.
        public DashboardDeprecatedSchemaIdDocumentsTest()
        {
            var engineMock = new Mock<IDbContextEngine>();
            engineMock.Setup(engine => engine.Identifier).Returns(DbContextIdentifier);
            engineMock.Setup(engine => engine.MapRegistry.MapsByModelType).Returns(new Dictionary<Type, IMap>());
            engineMock.Setup(engine => engine.Options).Returns(new DbContextOptions());

            dbContextMock = new Mock<IDbContext>();
            dbContextMock.Setup(dbContext => dbContext.Engine).Returns(engineMock.Object);

            repositoryMock = BuildRepositoryMock(RepositoryName, isReadOnly: false);
            var repositoryRegistryMock = new Mock<IRepositoryRegistry>();
            repositoryRegistryMock.Setup(registry => registry.Repositories)
                .Returns([repositoryMock.Object, BuildRepositoryMock(ReadOnlyRepositoryName, isReadOnly: true).Object]);
            dbContextMock.Setup(dbContext => dbContext.RepositoryRegistry).Returns(repositoryRegistryMock.Object);
        }

        // Tests.
        [Fact]
        public async Task CountReportsThroughTheGetHandler()
        {
            // Setup.
            repositoryMock.Setup(repo => repo.CountDeprecatedSchemaIdDocumentsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(7);
            using var host = await StartDashboardHostAsync();

            // Action.
            var response = await host.GetTestClient().GetAsync(new Uri(
                $"{PagePath}?handler=DeprecatedSchemaIdDocuments&identifier={DbContextIdentifier}&repositoryName={RepositoryName}",
                UriKind.Relative));

            // Assert.
            response.EnsureSuccessStatusCode();
            var responseJson = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"isUnavailable\":false", responseJson, StringComparison.Ordinal);
            Assert.Contains("\"documentsCount\":7", responseJson, StringComparison.Ordinal);
        }

        [Fact]
        public async Task CountReportsUnavailableDuringExclusiveAccess()
        {
            /* An exclusive access (a running migration) denies reads on the collection: the
             * handler reports the collection unavailable, instead of failing the request. */

            // Setup.
            repositoryMock.Setup(repo => repo.CountDeprecatedSchemaIdDocumentsAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new UnauthorizedAccessException());
            using var host = await StartDashboardHostAsync();

            // Action.
            var response = await host.GetTestClient().GetAsync(new Uri(
                $"{PagePath}?handler=DeprecatedSchemaIdDocuments&identifier={DbContextIdentifier}&repositoryName={RepositoryName}",
                UriKind.Relative));

            // Assert.
            response.EnsureSuccessStatusCode();
            Assert.Contains(
                "\"isUnavailable\":true",
                await response.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        [Fact]
        public async Task PageRendersTheDeprecatedSchemaIdElementsSection()
        {
            // Setup.
            using var host = await StartDashboardHostAsync();

            // Action.
            var response = await host.GetTestClient().GetAsync(new Uri(PagePath, UriKind.Relative));

            // Assert.
            response.EnsureSuccessStatusCode();
            var pageHtml = await response.Content.ReadAsStringAsync();
            Assert.Contains("Deprecated schema id elements", pageHtml, StringComparison.Ordinal);

            //the count is a read: every repository gets its control, the read-only ones too
            Assert.Equal(2, Regex.Matches(pageHtml, "data-role=\"count-deprecated-schema-ids\"").Count);

            /* What the count counts is repaired by the deprecated schemas rewrite of a
             * migration start, not by a control of its own. */
            Assert.DoesNotContain("data-role=\"migrate-deprecated-schema-ids\"", pageHtml, StringComparison.Ordinal);
        }

        // Helpers.
        private Mock<IRepository> BuildRepositoryMock(string name, bool isReadOnly)
        {
            var repositoryMock = new Mock<IRepository>();
            repositoryMock.Setup(repo => repo.DbContext).Returns(dbContextMock.Object);
            repositoryMock.Setup(repo => repo.IsReadOnly).Returns(isReadOnly);
            repositoryMock.Setup(repo => repo.ModelType).Returns(typeof(object));
            repositoryMock.Setup(repo => repo.Name).Returns(name);
            return repositoryMock;
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
    }
}
