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
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.AspNetCore.UI
{
    /* SCR-117: the dashboard finds and removes the references to missing origin documents,
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
        private readonly Mock<IDbContextEngine> engineMock;
        private readonly Mock<IRepository> readOnlyRepositoryMock;
        private readonly Mock<IRepository> repositoryMock;

        // Constructor.
        public DashboardMissingOriginReferencesTest()
        {
            engineMock = new Mock<IDbContextEngine>();
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
                    [new MissingOriginReferencesPathReport(
                        "Author",
                        ["authors"],
                        OriginDeleteMode.DeleteReferencingDocument,
                        2,
                        ["brokenId1", "brokenId2"],
                        5,
                        ["referencingId1", "referencingId2"])],
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
            //the declared policy travels with the path: it is the default action of a repair
            Assert.Contains(
                "\"originDelete\":\"DeleteReferencingDocument\"",
                responseJson,
                StringComparison.Ordinal);
            //the documents a repair would touch, so an operator can look them up first
            Assert.Contains(
                "\"trackedReferencingDocumentIds\":[\"referencingId1\",\"referencingId2\"]",
                responseJson,
                StringComparison.Ordinal);
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

            //every repository gets its scan control, only the writable one gets the repair
            Assert.Equal(2, Regex.Matches(pageHtml, "data-role=\"scan-references\"").Count);
            var repairMatches = Regex.Matches(pageHtml, "data-role=\"repair-references\"");
            Assert.Single(repairMatches);
            //the repair runs as an operation: it offers a dry run, like a migration start
            Assert.Single(Regex.Matches(pageHtml, "data-role=\"repair-references-dry-run\""));

            //the repair control belongs to the writable repository block of the section
            var sectionHtml = pageHtml[pageHtml.IndexOf("missing-origin-references", StringComparison.Ordinal)..];
            var writableBlockStart = sectionHtml.IndexOf($"data-repository=\"{RepositoryName}\"", StringComparison.Ordinal);
            var readOnlyBlockStart = sectionHtml.IndexOf($"data-repository=\"{ReadOnlyRepositoryName}\"", StringComparison.Ordinal);
            var repairIndex = Regex.Match(sectionHtml, "data-role=\"repair-references\"").Index;
            Assert.InRange(repairIndex, writableBlockStart, readOnlyBlockStart);
        }

        [Fact]
        public async Task RepairIsRejectedOnAReadOnlyRepository()
        {
            /* The page doesn't render the repair controls on a read-only repository, but the
             * request doesn't have to come from them. */

            // Setup.
            using var host = await StartDashboardHostAsync();

            // Action.
            var response = await PostRepairReferencesAsync(
                host, ReadOnlyRepositoryName, [("Author", "RemoveReference")]);

            // Assert.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var responseJson = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"started\":false", responseJson, StringComparison.Ordinal);
            Assert.Contains("read-only", responseJson, StringComparison.Ordinal);
            VerifyNoRepairStarted();
        }

        [Theory]
        //a value that isn't a mode, and a numeric one parsing as the enum without being a mode
        [InlineData("DropTheCollection")]
        [InlineData("42")]
        public async Task RepairIsRejectedWithAnUnknownMode(string repairMode)
        {
            // Setup.
            using var host = await StartDashboardHostAsync();

            // Action.
            var response = await PostRepairReferencesAsync(host, RepositoryName, [("Author", repairMode)]);

            // Assert.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(
                "\"started\":false",
                await response.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
            VerifyNoRepairStarted();
        }

        [Fact]
        public async Task RepairIsRejectedWithAnUnusableLockLeaseDuration()
        {
            /* The repair claims the same db context lock a migration claims, with the lease
             * duration of the card: an unbounded one would keep the db context locked for as
             * long as it says if this instance dies. */

            // Setup.
            using var host = await StartDashboardHostAsync();

            // Action.
            var response = await PostRepairReferencesAsync(
                host,
                RepositoryName,
                [("Author", "RemoveReference")],
                lockLeaseDurationMinutes: (IndexModel.MaxLockLeaseDurationMinutes + 1).ToString(CultureInfo.InvariantCulture));

            // Assert.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(
                IndexModel.MaxLockLeaseDurationMinutes.ToString(CultureInfo.InvariantCulture),
                await response.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
            VerifyNoRepairStarted();
        }

        [Fact]
        public async Task RepairStartsAnOperationWithTheChosenModes()
        {
            // Setup.
            IReadOnlyDictionary<string, OriginDeleteMode>? repairModes = null;
            dbContextMock.Setup(dbContext => dbContext.TryStartReferencesRepairAsync(
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyDictionary<string, OriginDeleteMode>>(),
                    It.IsAny<bool>(),
                    It.IsAny<TimeSpan?>()))
                .Callback((string _, IReadOnlyDictionary<string, OriginDeleteMode> modes, bool _, TimeSpan? _) =>
                    repairModes = modes)
                .ReturnsAsync(new ReferencesRepairOperation(
                    engineMock.Object,
                    RepositoryName,
                    new Dictionary<string, OriginDeleteMode>()));
            using var host = await StartDashboardHostAsync();

            // Action.
            var response = await PostRepairReferencesAsync(
                host,
                RepositoryName,
                [("Author", "RemoveReference"), ("Editor", "DeleteReferencingDocument")],
                dryRun: true);

            // Assert.
            response.EnsureSuccessStatusCode();
            Assert.Contains(
                "\"started\":true",
                await response.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);

            //the action chosen for each path is what the start forwards, with the dry run flag
            Assert.NotNull(repairModes);
            Assert.Equal(OriginDeleteMode.RemoveReference, repairModes["Author"]);
            Assert.Equal(OriginDeleteMode.DeleteReferencingDocument, repairModes["Editor"]);
            dbContextMock.Verify(
                dbContext => dbContext.TryStartReferencesRepairAsync(
                    RepositoryName,
                    It.IsAny<IReadOnlyDictionary<string, OriginDeleteMode>>(),
                    true,
                    TimeSpan.FromMinutes(25)),
                Times.Once());
        }

        // Helpers.
        private void VerifyNoRepairStarted() =>
            dbContextMock.Verify(
                dbContext => dbContext.TryStartReferencesRepairAsync(
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyDictionary<string, OriginDeleteMode>>(),
                    It.IsAny<bool>(),
                    It.IsAny<TimeSpan?>()),
                Times.Never());

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

        private static async Task<HttpResponseMessage> PostRepairReferencesAsync(
            IHost host,
            string repositoryName,
            (string ElementPath, string RepairMode)[] repairModes,
            bool dryRun = false,
            string lockLeaseDurationMinutes = "25")
        {
            var client = host.GetTestClient();

            var pageResponse = await client.GetAsync(new Uri(PagePath, UriKind.Relative));
            pageResponse.EnsureSuccessStatusCode();
            var (token, cookie) = await ExtractAntiforgeryAsync(pageResponse);

            //the page sends the chosen actions as two parallel lists, one entry per path
            List<KeyValuePair<string, string>> form =
            [
                new("identifier", DbContextIdentifier),
                new("repositoryName", repositoryName),
                new("dryRun", dryRun.ToString(CultureInfo.InvariantCulture)),
                new("lockLeaseDurationMinutes", lockLeaseDurationMinutes)
            ];
            foreach (var (elementPath, repairMode) in repairModes)
            {
                form.Add(new KeyValuePair<string, string>("elementPaths", elementPath));
                form.Add(new KeyValuePair<string, string>("repairModes", repairMode));
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, PagePath + "?handler=RepairMissingOriginReferences")
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
