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
    /* MODM-117: the dashboard finds and removes the references to missing origin documents,
     * one collection at a time, delegating the scans to the collection repository. */
    public class DashboardMissingOriginReferencesTest
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
        private readonly Mock<IRepository> readOnlyRepositoryMock;
        private readonly Mock<IRepository> repositoryMock;

        // Constructor.
        public DashboardMissingOriginReferencesTest()
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
        public async Task FindReportsThroughTheGetHandler()
        {
            // Setup.
            repositoryMock.Setup(repo => repo.FindMissingOriginReferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MissingOriginReferencesReport(
                    [new MissingOriginReferencesPathReport("Author", ["authors"], 2, ["brokenId1", "brokenId2"], 5)],
                    ["Labels"]));
            using var host = await StartDashboardHostAsync();

            // Action.
            var response = await host.GetTestClient().GetAsync(new Uri(
                $"{PagePath}?handler=MissingOriginReferences&identifier={DbContextIdentifier}&repositoryName={RepositoryName}",
                UriKind.Relative));

            // Assert.
            response.EnsureSuccessStatusCode();
            var responseJson = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"isUnavailable\":false", responseJson, StringComparison.Ordinal);
            Assert.Contains("\"elementPath\":\"Author\"", responseJson, StringComparison.Ordinal);
            Assert.Contains("\"originRepositoryNames\":[\"authors\"]", responseJson, StringComparison.Ordinal);
            Assert.Contains("\"missingOriginIdsCount\":2", responseJson, StringComparison.Ordinal);
            Assert.Contains("\"trackedMissingOriginIds\":[\"brokenId1\",\"brokenId2\"]", responseJson, StringComparison.Ordinal);
            Assert.Contains("\"referencingDocumentsCount\":5", responseJson, StringComparison.Ordinal);
            Assert.Contains("\"unverifiableElementPaths\":[\"Labels\"]", responseJson, StringComparison.Ordinal);
        }

        [Fact]
        public async Task FindReportsUnavailableDuringExclusiveAccess()
        {
            /* An exclusive access (a running migration) denies reads on the collection: the
             * handler reports the collection unavailable, instead of failing the request. */

            // Setup.
            repositoryMock.Setup(repo => repo.FindMissingOriginReferencesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new UnauthorizedAccessException());
            using var host = await StartDashboardHostAsync();

            // Action.
            var response = await host.GetTestClient().GetAsync(new Uri(
                $"{PagePath}?handler=MissingOriginReferences&identifier={DbContextIdentifier}&repositoryName={RepositoryName}",
                UriKind.Relative));

            // Assert.
            response.EnsureSuccessStatusCode();
            Assert.Contains(
                "\"isUnavailable\":true",
                await response.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        [Fact]
        public async Task PageRendersTheMissingOriginReferencesSection()
        {
            // Setup.
            using var host = await StartDashboardHostAsync();

            // Action.
            var response = await host.GetTestClient().GetAsync(new Uri(PagePath, UriKind.Relative));

            // Assert.
            response.EnsureSuccessStatusCode();
            var pageHtml = await response.Content.ReadAsStringAsync();
            Assert.Contains("Missing origin references", pageHtml, StringComparison.Ordinal);

            //every repository gets its scan control, only the writable one gets the removal
            Assert.Equal(2, Regex.Matches(pageHtml, "data-role=\"scan-references\"").Count);
            var removalMatches = Regex.Matches(pageHtml, "data-role=\"remove-references\"");
            Assert.Single(removalMatches);

            //the removal control belongs to the writable repository block of the section
            var sectionHtml = pageHtml[pageHtml.IndexOf("missing-origin-references", StringComparison.Ordinal)..];
            var writableBlockStart = sectionHtml.IndexOf($"data-repository=\"{RepositoryName}\"", StringComparison.Ordinal);
            var readOnlyBlockStart = sectionHtml.IndexOf($"data-repository=\"{ReadOnlyRepositoryName}\"", StringComparison.Ordinal);
            var removalIndex = Regex.Match(sectionHtml, "data-role=\"remove-references\"").Index;
            Assert.InRange(removalIndex, writableBlockStart, readOnlyBlockStart);
        }

        [Fact]
        public async Task RemovalIsRejectedOnAReadOnlyRepository()
        {
            /* The page doesn't render the removal control on a read-only repository, but the
             * request doesn't have to come from it. */

            // Setup.
            using var host = await StartDashboardHostAsync();

            // Action.
            var response = await PostRemoveReferencesAsync(host, ReadOnlyRepositoryName);

            // Assert.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var responseJson = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"removed\":false", responseJson, StringComparison.Ordinal);
            Assert.Contains("read-only", responseJson, StringComparison.Ordinal);
            readOnlyRepositoryMock.Verify(
                repo => repo.RemoveMissingOriginReferencesAsync(It.IsAny<CancellationToken>()),
                Times.Never());
        }

        [Fact]
        public async Task RemovalRunsThroughThePostHandler()
        {
            // Setup.
            repositoryMock.Setup(repo => repo.RemoveMissingOriginReferencesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MissingOriginReferencesRemovalReport(
                    [new MissingOriginReferencesPathRemoval("Author", 2, 5)],
                    []));
            using var host = await StartDashboardHostAsync();

            // Action.
            var response = await PostRemoveReferencesAsync(host, RepositoryName);

            // Assert.
            response.EnsureSuccessStatusCode();
            var responseJson = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"removed\":true", responseJson, StringComparison.Ordinal);
            Assert.Contains("\"elementPath\":\"Author\"", responseJson, StringComparison.Ordinal);
            Assert.Contains("\"missingOriginIdsCount\":2", responseJson, StringComparison.Ordinal);
            Assert.Contains("\"updatedDocumentsCount\":5", responseJson, StringComparison.Ordinal);
            repositoryMock.Verify(
                repo => repo.RemoveMissingOriginReferencesAsync(It.IsAny<CancellationToken>()),
                Times.Once());
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

        private static async Task<HttpResponseMessage> PostRemoveReferencesAsync(IHost host, string repositoryName)
        {
            var client = host.GetTestClient();

            var pageResponse = await client.GetAsync(new Uri(PagePath, UriKind.Relative));
            pageResponse.EnsureSuccessStatusCode();
            var (token, cookie) = await ExtractAntiforgeryAsync(pageResponse);

            using var request = new HttpRequestMessage(HttpMethod.Post, PagePath + "?handler=RemoveMissingOriginReferences")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["identifier"] = DbContextIdentifier,
                    ["repositoryName"] = repositoryName
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
