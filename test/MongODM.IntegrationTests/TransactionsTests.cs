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

using Etherna.MongoDB.Driver.Linq;
using Etherna.MongODM.Core.ExecContext.AsyncLocal;
using Etherna.MongODM.IntegrationTests.Fixtures;
using Etherna.MongODM.IntegrationTests.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.MongODM.IntegrationTests
{
    [Collection("Integration")]
    public class TransactionsTests : IDisposable
    {
        // Fields.
        private readonly ITestDbContext dbContext;
        private readonly IntegrationFixture fixture;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public TransactionsTests(IntegrationFixture fixture)
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
        public async Task AbortedTransactionDiscardsAllChanges()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var existingPost = new Post("title", "content");
            await dbContext.Posts.CreateAsync(existingPost);

            // Action.
            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            Post? createdPost = null;
            await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.ExecuteInTransactionAsync(async () =>
            {
                createdPost = new Post("tx title", "tx content");
                await dbContext.Posts.CreateAsync(createdPost);

                var loadedPost = await dbContext.Posts.FindOneAsync(existingPost.Id);
                loadedPost.Content = "updated content";
                await dbContext.SaveChangesAsync();

                throw new InvalidOperationException();
            }));

            // Assert.
            //the aborted transaction discarded the create and the changes save
            using var readScope = fixture.ServiceProvider.CreateScope();
            var readDbContext = readScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            Assert.Null(await readDbContext.Posts.TryFindOneAsync(createdPost!.Id));

            var foundExistingPost = await readDbContext.Posts.FindOneAsync(existingPost.Id);
            Assert.Equal("content", foundExistingPost.Content);
        }

        [Fact]
        public async Task CommittedTransactionPersistsAllChanges()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var existingPost = new Post("title", "content");
            await dbContext.Posts.CreateAsync(existingPost);

            // Action.
            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            Post? createdPost = null;
            await dbContext.ExecuteInTransactionAsync(async () =>
            {
                createdPost = new Post("tx title", "tx content");
                await dbContext.Posts.CreateAsync(createdPost);

                var loadedPost = await dbContext.Posts.FindOneAsync(existingPost.Id);
                loadedPost.Content = "updated content";
                await dbContext.SaveChangesAsync();
            });

            // Assert.
            using var readScope = fixture.ServiceProvider.CreateScope();
            var readDbContext = readScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var foundCreatedPost = await readDbContext.Posts.FindOneAsync(createdPost!.Id);
            Assert.Equal("tx title", foundCreatedPost.Title);

            var foundExistingPost = await readDbContext.Posts.FindOneAsync(existingPost.Id);
            Assert.Equal("updated content", foundExistingPost.Content);
        }

        [Fact]
        public async Task TransactionalSaveChangesPersistsMultipleModels()
        {
            /* Transactions are enabled by default and the fixture deployment is a replica
             * set: this save of two changed models runs into a single implicit transaction. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post0 = new Post("title0", "content0");
            var post1 = new Post("title1", "content1");
            await dbContext.Posts.CreateAsync(post0);
            await dbContext.Posts.CreateAsync(post1);

            // Action.
            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedPost0 = await dbContext.Posts.FindOneAsync(post0.Id);
            var loadedPost1 = await dbContext.Posts.FindOneAsync(post1.Id);
            loadedPost0.Content = "updated content0";
            loadedPost1.Content = "updated content1";
            await dbContext.SaveChangesAsync();

            // Assert.
            using var readScope = fixture.ServiceProvider.CreateScope();
            var readDbContext = readScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            Assert.Equal("updated content0", (await readDbContext.Posts.FindOneAsync(post0.Id)).Content);
            Assert.Equal("updated content1", (await readDbContext.Posts.FindOneAsync(post1.Id)).Content);
        }

        [Fact]
        public async Task ReadsInsideTransactionSeeUncommittedChanges()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            string? createdPostId = null;

            // Action.
            await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.ExecuteInTransactionAsync(async () =>
            {
                var createdPost = new Post("tx title", "tx content");
                await dbContext.Posts.CreateAsync(createdPost);
                createdPostId = createdPost.Id;

                //the transactional query reads the uncommitted document
                var queriedTitles = await dbContext.Posts.QueryElementsAsync(elements =>
                    elements.Where(p => p.Id == createdPostId)
                            .Select(p => p.Title)
                            .ToListAsync());
                Assert.Equal("tx title", Assert.Single(queriedTitles));

                //abort to prove the read didn't come from committed state
                throw new InvalidOperationException();
            }));

            // Assert.
            using var readScope = fixture.ServiceProvider.CreateScope();
            var readDbContext = readScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            Assert.Null(await readDbContext.Posts.TryFindOneAsync(createdPostId!));
        }
    }
}
