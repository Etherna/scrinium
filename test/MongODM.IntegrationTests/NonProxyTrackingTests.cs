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
using System;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.MongODM.IntegrationTests
{
    [Collection("Integration")]
    public class NonProxyTrackingTests : IDisposable
    {
        // Fields.
        private readonly ITestDbContext dbContext;
        private readonly IntegrationFixture fixture;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public NonProxyTrackingTests(IntegrationFixture fixture)
        {
            this.fixture = fixture;
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
        public async Task ChangesOfACreatedInstanceAreTracked()
        {
            /* MODM-38: a model created with a plain constructor is not a proxy, but its
             * changes after the create must still be saved. Change tracking is snapshot
             * based and doesn't depend on the model being a proxy. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("title", "content");
            await dbContext.Posts.CreateAsync(post);

            // Action.
            //mutate the same created (non proxy) instance, then save
            post.Content = "updated content";
            await dbContext.SaveChangesAsync();

            // Assert.
            using var readScope = fixture.ServiceProvider.CreateScope();
            var readDbContext = readScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var foundPost = await readDbContext.Posts.FindOneAsync(post.Id);
            Assert.Equal("updated content", foundPost.Content);
            Assert.Equal("title", foundPost.Title);
        }

        [Fact]
        public async Task InPlaceCollectionMutationOnALoadedModelIsTracked()
        {
            /* Non regression for the snapshot diff: a business method that mutates a backing
             * field in place (bypassing the property setter) must still be detected as a
             * change and persisted, as the previous PropertyAlterer based tracking did. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("post title", "post content");
            await dbContext.Posts.CreateAsync(post);
            var blog = new Blog("blog title");
            await dbContext.Blogs.CreateAsync(blog);

            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedBlog = await dbContext.Blogs.FindOneAsync(blog.Id);

            // Action.
            //AddPost mutates the _posts backing field in place and sets LastPost
            loadedBlog.AddPost(post);
            await dbContext.SaveChangesAsync();

            // Assert.
            using var readScope = fixture.ServiceProvider.CreateScope();
            var readDbContext = readScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var foundBlog = await readDbContext.Blogs.FindOneAsync(blog.Id);
            Assert.Equal(post.Id, foundBlog.LastPost!.Id);
            Assert.Single(foundBlog.Posts);
        }

        [Fact]
        public async Task ReplaceConvertsADocumentToANewTypeInPlace()
        {
            /* MODM-83: a document handled by a base type repository can change its concrete type
             * keeping the same id. The converted instance is a brand new, non proxy object: the
             * replace must persist it, upgrading the stored document to the new type. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var web2Account = new Web2Account("alice");
            await dbContext.Accounts.CreateAsync(web2Account);

            // Action.
            //convert to a new instance of a different type, same id, and replace
            var web3Account = new Web3Account(web2Account, "0xabc");
            await dbContext.Accounts.ReplaceAsync(web3Account);

            // Assert.
            using var readScope = fixture.ServiceProvider.CreateScope();
            var readDbContext = readScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var foundAccount = await readDbContext.Accounts.FindOneAsync(web2Account.Id);
            var foundWeb3Account = Assert.IsAssignableFrom<Web3Account>(foundAccount);
            Assert.Equal("alice", foundWeb3Account.Username);
            Assert.Equal("0xabc", foundWeb3Account.EtherAddress);
        }
    }
}
