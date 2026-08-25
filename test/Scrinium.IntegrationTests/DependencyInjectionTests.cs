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

using Etherna.MongODM.Core.ExecContext.AsyncLocal;
using Etherna.MongODM.IntegrationTests.Fixtures;
using Etherna.MongODM.IntegrationTests.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.MongODM.IntegrationTests
{
    [Collection("Integration")]
    public class DependencyInjectionTests(IntegrationFixture fixture)
    {
        // Tests.
        [Fact]
        public async Task ProxyTypesAreRecognizedAcrossDbContexts()
        {
            /* Proxy types must be recognized by every db context of the process, also when
             * the proxy has been created through another one. The proxy generator is a
             * process wide singleton: a per-engine registry would break type purging for
             * CLR types mapped in more than one db context. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("title", "content");
            await fixture.TestDbContext.Posts.CreateAsync(post);

            var loadedPost = await fixture.TestDbContext.Posts.FindOneAsync(post.Id);
            var proxyType = loadedPost.GetType();

            // Assert.
            //sanity check: loaded models are proxies
            Assert.NotEqual(typeof(Post), proxyType);

            //another db context recognizes and purges the proxy type
            Assert.True(fixture.SecondDbContext.Engine.ProxyGenerator.IsProxyType(proxyType));
            Assert.Equal(typeof(Post), fixture.SecondDbContext.Engine.ProxyGenerator.PurgeProxyType(proxyType));
        }

        [Fact]
        public void SameScopeResolvesSameDbContextInstance()
        {
            // Action.
            using var serviceScope = fixture.ServiceProvider.CreateScope();
            var dbContext0 = serviceScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            var dbContext1 = serviceScope.ServiceProvider.GetRequiredService<ITestDbContext>();

            // Assert.
            Assert.Same(dbContext0, dbContext1);
        }

        [Fact]
        public async Task ScopedDbContextsShareTheSameEngine()
        {
            // Action.
            using var serviceScope0 = fixture.ServiceProvider.CreateScope();
            using var serviceScope1 = fixture.ServiceProvider.CreateScope();
            var dbContext0 = serviceScope0.ServiceProvider.GetRequiredService<ITestDbContext>();
            var dbContext1 = serviceScope1.ServiceProvider.GetRequiredService<ITestDbContext>();

            // Assert.
            //each scope gets its own db context instance, attached to the same engine
            Assert.NotSame(dbContext0, dbContext1);
            Assert.Same(dbContext0.Engine, dbContext1.Engine);
            Assert.NotSame(dbContext0.RepositoryRegistry, dbContext1.RepositoryRegistry);

            //both instances work against the same database
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("title", "content");
            await dbContext0.Posts.CreateAsync(post);

            var foundPost = await dbContext1.Posts.FindOneAsync(post.Id);
            Assert.Equal(post.Id, foundPost.Id);
        }
    }
}
