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

using Etherna.Scrinium.AspNetCore.UI.Areas.MongODM.Pages;
using Etherna.Scrinium.AspNetCore.UI.Auth.Filters;
using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.Domain.Models.DbMigrationOpAgg;
using Etherna.Scrinium.Core.Migration;
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
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.AspNetCore.UI
{
    /* MODM-256: the dashboard counts the documents carrying their schema id under a deprecated
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
        private const string PagePath = "/MongODM";
        private const string ReadOnlyRepositoryName = "readonlyNotes";
        private const string RepositoryName = "posts";

        // Fields.
        private readonly Mock<IDbContext> dbContextMock;
        private readonly Mock<IRepository> readOnlyRepositoryMock;
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
            readOnlyRepositoryMock = BuildRepositoryMock(ReadOnlyRepositoryName, isReadOnly: true);

            var repositoryRegistryMock = new Mock<IRepositoryRegistry>();
            repositoryRegistryMock.Setup(registry => registry.Repositories)
                .Returns([repositoryMock.Object, readOnlyRepositoryMock.Object]);
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
        public async Task MigrationIsRejectedOnAReadOnlyRepository()
        {
            /* The page doesn't render the migration control on a read-only repository, but the
             * request doesn't have to come from it. */

            // Setup.
            using var host = await StartDashboardHostAsync();

            // Action.
            var response = await PostMigrateDocumentsAsync(host, ReadOnlyRepositoryName);

            // Assert.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var responseJson = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"migrated\":false", responseJson, StringComparison.Ordinal);
            Assert.Contains("read-only", responseJson, StringComparison.Ordinal);
            readOnlyRepositoryMock.Verify(
                repo => repo.MigrateDeprecatedSchemaIdDocumentsAsync(It.IsAny<CancellationToken>()),
                Times.Never());
        }

        [Fact]
        public async Task MigrationReportsTheFailingDocumentsThroughThePostHandler()
        {
            /* A migration reports what failed instead of throwing: the failing documents reach
             * the page with the errors that skipped them. */

            // Setup.
            repositoryMock.Setup(repo => repo.MigrateDeprecatedSchemaIdDocumentsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(MigrationResult.Failed(
                    4,
                    documentErrors: [new DocumentMigrationError("brokenId", "FormatException: invalid content")],
                    totDocumentErrors: 1));
            using var host = await StartDashboardHostAsync();

            // Action.
            var response = await PostMigrateDocumentsAsync(host, RepositoryName);

            // Assert.
            response.EnsureSuccessStatusCode();
            var responseJson = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"migrated\":false", responseJson, StringComparison.Ordinal);
            Assert.Contains("\"migratedDocumentsCount\":4", responseJson, StringComparison.Ordinal);
            Assert.Contains("\"documentErrorsCount\":1", responseJson, StringComparison.Ordinal);
            Assert.Contains("\"documentId\":\"brokenId\"", responseJson, StringComparison.Ordinal);
            Assert.Contains("invalid content", responseJson, StringComparison.Ordinal);
        }

        [Fact]
        public async Task MigrationReportsUnavailableDuringExclusiveAccess()
        {
            /* An exclusive access denying the collection fails the whole scan: the migration
             * result carries its exception, and the handler reports it as unavailable. */

            // Setup.
            repositoryMock.Setup(repo => repo.MigrateDeprecatedSchemaIdDocumentsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(MigrationResult.Failed(0, new UnauthorizedAccessException()));
            using var host = await StartDashboardHostAsync();

            // Action.
            var response = await PostMigrateDocumentsAsync(host, RepositoryName);

            // Assert.
            response.EnsureSuccessStatusCode();
            Assert.Contains(
                "an exclusive access is running",
                await response.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        [Fact]
        public async Task MigrationRunsThroughThePostHandler()
        {
            // Setup.
            repositoryMock.Setup(repo => repo.MigrateDeprecatedSchemaIdDocumentsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(MigrationResult.Succeeded(5));
            using var host = await StartDashboardHostAsync();

            // Action.
            var response = await PostMigrateDocumentsAsync(host, RepositoryName);

            // Assert.
            response.EnsureSuccessStatusCode();
            var responseJson = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"migrated\":true", responseJson, StringComparison.Ordinal);
            Assert.Contains("\"migratedDocumentsCount\":5", responseJson, StringComparison.Ordinal);
            Assert.Contains("\"documentErrorsCount\":0", responseJson, StringComparison.Ordinal);
            repositoryMock.Verify(
                repo => repo.MigrateDeprecatedSchemaIdDocumentsAsync(It.IsAny<CancellationToken>()),
                Times.Once());
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

            //every repository gets its count control, only the writable one gets the migration
            Assert.Equal(2, Regex.Matches(pageHtml, "data-role=\"count-deprecated-schema-ids\"").Count);
            Assert.Single(Regex.Matches(pageHtml, "data-role=\"migrate-deprecated-schema-ids\""));

            //the migration control belongs to the writable repository block of the section
            var sectionHtml = pageHtml[pageHtml.IndexOf("deprecated-schema-ids", StringComparison.Ordinal)..];
            var writableBlockStart = sectionHtml.IndexOf($"data-repository=\"{RepositoryName}\"", StringComparison.Ordinal);
            var readOnlyBlockStart = sectionHtml.IndexOf($"data-repository=\"{ReadOnlyRepositoryName}\"", StringComparison.Ordinal);
            var migrationIndex = Regex.Match(sectionHtml, "data-role=\"migrate-deprecated-schema-ids\"").Index;
            Assert.InRange(migrationIndex, writableBlockStart, readOnlyBlockStart);
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

        private static async Task<HttpResponseMessage> PostMigrateDocumentsAsync(IHost host, string repositoryName)
        {
            var client = host.GetTestClient();

            var pageResponse = await client.GetAsync(new Uri(PagePath, UriKind.Relative));
            pageResponse.EnsureSuccessStatusCode();
            var (token, cookie) = await ExtractAntiforgeryAsync(pageResponse);

            using var request = new HttpRequestMessage(HttpMethod.Post, PagePath + "?handler=MigrateDeprecatedSchemaIdDocuments")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["identifier"] = DbContextIdentifier,
                    ["repositoryName"] = repositoryName
                })
            };
            request.Headers.Add("Cookie", cookie);
            //same header sent by mongodmDash.js
            request.Headers.Add("RequestVerificationToken", token);

            return await client.SendAsync(request);
        }

        private async Task<IHost> StartDashboardHostAsync() =>
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
                        services.AddMongODMAdminDashboard(new DashboardOptions
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
