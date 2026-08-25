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

using Etherna.MongoDB.Driver;
using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Etherna.Scrinium.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.IntegrationTests
{
    /* MODM-98: the blogs repository declares a custom index on the id path of its last
     * post reference. The server denies two indexes with the same key pattern, so the
     * automatic reference index has to leave the field to the custom one. */
    [Collection("Integration")]
    public class IndexBuildingTests : IDisposable
    {
        // Fields.
        private readonly ITestDbContext dbContext;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public IndexBuildingTests(IntegrationFixture fixture)
        {
            ArgumentNullException.ThrowIfNull(fixture);
            serviceScope = fixture.ServiceProvider.CreateScope();
            dbContext = serviceScope.ServiceProvider.GetRequiredService<ITestDbContext>();
        }

        public void Dispose()
        {
            serviceScope.Dispose();
            GC.SuppressFinalize(this);
        }

        // Tests.
        [Fact]
        public async Task CustomIndexOnAReferenceIdPathReplacesTheAutomaticIndex()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            // Action.
            //idempotent, the db context seeding already built the same indexes
            await dbContext.Blogs.BuildNewIndexesAsync();

            // Assert.
            var indexes = await dbContext.Blogs.AccessToCollectionAsync(async collection =>
                await (await collection.Indexes.ListAsync()).ToListAsync());
            var indexNames = indexes.Select(i => i["name"].AsString).ToList();

            //the custom index on the reference id path, without an automatic duplicate
            Assert.DoesNotContain("ref_LastPost._id", indexNames);
            var customIndex = indexes.Single(i => i["name"] == "blog_last_post");
            Assert.Equal(1, customIndex["key"]["LastPost._id"].ToInt32());

            //the reference without a custom index keeps its automatic index
            var automaticIndex = indexes.Single(i => i["name"] == "ref_Posts._id");
            Assert.Equal(1, automaticIndex["key"]["Posts._id"].ToInt32());
            Assert.True(automaticIndex["sparse"].AsBoolean);
        }
    }
}
