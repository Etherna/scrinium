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

using Etherna.Scrinium.AspNetCore;
using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Etherna.Scrinium.Core.Options;
using Etherna.Scrinium.IntegrationTests.Fixtures;
using Etherna.Scrinium.IntegrationTests.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.IntegrationTests
{
    [Collection("Integration")]
    public class DependencyInjectionTests(IntegrationFixture fixture)
    {
        // Tests.
        [Fact]
        public void AddDbContextDeniesChildrenDeclaredInCycle()
        {
            /* Db contexts declaring each other as children, directly or through other ones,
             * could neither resolve their scoped instances nor seed: the registration closing
             * the cycle must fail fast, detailing it. */

            // Setup.
            var configuration = new ScriniumConfiguration(new ServiceCollection());
            configuration
                .AddDbContext<ITestDbContext, TestDbContext>(
                    _ => new TestDbContext(),
                    options => options.ParentFor<ISecondDbContext>())
                .AddDbContext<ISecondDbContext, SecondDbContext>(
                    _ => new SecondDbContext(),
                    options => options.ParentFor<IParentDbContext>());

            // Action & assert.
            var exception = Assert.Throws<InvalidOperationException>(
                () => configuration.AddDbContext<IParentDbContext, ParentDbContext>(
                    _ => new ParentDbContext(),
                    options => options.ParentFor<ITestDbContext>()));
            Assert.Contains(nameof(DbContextOptions.ParentFor), exception.Message, StringComparison.Ordinal);
            Assert.Contains(
                $"{nameof(IParentDbContext)} -> {nameof(ITestDbContext)} -> {nameof(ISecondDbContext)} -> {nameof(IParentDbContext)}",
                exception.Message,
                StringComparison.Ordinal);
        }

        [Fact]
        public async Task ProxyTypesAreRecognizedAcrossDbContexts()
        {
            /* Proxy types must be recognized by every db context of the process, also when
             * the proxy has been created through another one. The proxy generator is a
             * process wide singleton: a per-engine registry would break type purging for
             * CLR types mapped in more than one db context. */

            // Setup.
            //create on a setup scope: the test scope loads the documents fresh
            using var setupScope = fixture.ServiceProvider.CreateScope();
            var setupDbContext = setupScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("title", "content");
            await setupDbContext.Posts.CreateAsync(post);

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
