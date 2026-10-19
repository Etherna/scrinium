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

using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Etherna.Scrinium.Core.ProxyModels;
using Etherna.Scrinium.Core.Repositories;
using Etherna.Scrinium.Core.Utility;
using Etherna.Scrinium.IntegrationTests.Fixtures;
using Etherna.Scrinium.IntegrationTests.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.IntegrationTests
{
    [Collection("Integration")]
    public class ParallelFlowsTests : IDisposable
    {
        // Fields.
        private readonly ITestDbContext dbContext;
        private readonly IntegrationFixture fixture;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        public ParallelFlowsTests(IntegrationFixture fixture)
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
        public async Task ParallelReadsOnASharedExecutionContextBindEachModelToItsRepository()
        {
            /* A request forking parallel reads shares its execution context items among them,
             * like the async local context initialized here before forking. Each flow must
             * resolve the repository of its own read, also while a sibling flow wraps its read
             * in a handler without repository (a store reading on behalf of a traversal): the
             * documents are distinct, so every read deserializes instead of hitting the
             * identity map. */
            const int rounds = 10;
            const int flowsPerRepository = 4;

            // Setup.
            //create on a setup scope: the test scope loads the documents fresh
            using var setupScope = fixture.ServiceProvider.CreateScope();
            var setupDbContext = setupScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            var posts = Enumerable.Range(0, rounds * flowsPerRepository)
                .Select(i => new Post($"title{i}", $"content{i}"))
                .ToArray();
            var blogs = Enumerable.Range(0, rounds * flowsPerRepository)
                .Select(i => new Blog($"blog{i}"))
                .ToArray();
            using (AsyncLocalContext.Instance.InitAsyncLocalContext())
            {
                await setupDbContext.Posts.CreateAsync(posts);
                await setupDbContext.Blogs.CreateAsync(blogs);
            }

            // Action.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            for (var round = 0; round < rounds; round++)
            {
                var flows = Enumerable.Range(0, flowsPerRepository).SelectMany(flow =>
                {
                    var index = round * flowsPerRepository + flow;
                    var wrapInRepositoryLessHandler = flow % 2 == 0;
                    return new[]
                    {
                        Task.Run(() => ReadAsync(dbContext.Posts, posts[index].Id, wrapInRepositoryLessHandler)),
                        Task.Run(() => ReadAsync(dbContext.Blogs, blogs[index].Id, wrapInRepositoryLessHandler))
                    };
                });
                await Task.WhenAll(flows);
            }

            // Helpers.
            async Task ReadAsync<TModel>(
                IRepository<TModel, string> repository,
                string id,
                bool wrapInRepositoryLessHandler)
                where TModel : class, IEntityModel<string>
            {
                using (wrapInRepositoryLessHandler ? new DbExecutionContextHandler(dbContext) : null)
                {
                    var model = await repository.FindOneAsync(id);

                    // Assert.
                    Assert.Same(repository, ((IReferenceable)model).SourceRepository);
                }
            }
        }
    }
}
