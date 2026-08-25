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

using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Etherna.Scrinium.IntegrationTests.Fixtures;
using Etherna.Scrinium.IntegrationTests.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.IntegrationTests
{
    [Collection("Integration")]
    public class ScopeIsolationTests : IDisposable
    {
        // Fields.
        private readonly ISecondDbContext secondDbContext;
        private readonly ITestDbContext testDbContext;
        private readonly IntegrationFixture fixture;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public ScopeIsolationTests(IntegrationFixture fixture)
        {
            this.fixture = fixture;
            serviceScope = fixture.ServiceProvider.CreateScope();
            secondDbContext = serviceScope.ServiceProvider.GetRequiredService<ISecondDbContext>();
            testDbContext = serviceScope.ServiceProvider.GetRequiredService<ITestDbContext>();
        }

        public void Dispose()
        {
            serviceScope.Dispose();
            GC.SuppressFinalize(this);
        }

        // Tests.
        [Fact]
        public async Task ParallelScopesDontShareTrackedModels()
        {
            /* Simulate two parallel background jobs, each with its own DI scope and so its
             * own db context instance, sharing the same engine. Changed models belong to the
             * instance: one job can't see, nor save, the pending changes of the other. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("title", "content");
            await testDbContext.Posts.CreateAsync(post);

            var job0Mutated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var job1Verified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            // Action.
            var job0 = Task.Run(async () =>
            {
                using var jobScope = fixture.ServiceProvider.CreateScope();
                var jobDbContext = jobScope.ServiceProvider.GetRequiredService<ITestDbContext>();
                using var jobContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

                var loadedPost = await jobDbContext.Posts.FindOneAsync(post.Id);
                loadedPost.Content = "updated by job0";

                Assert.Single(jobDbContext.ChangedModelsList);

                job0Mutated.SetResult();
                await job1Verified.Task;
            });

            var job1 = Task.Run(async () =>
            {
                await job0Mutated.Task;

                using var jobScope = fixture.ServiceProvider.CreateScope();
                var jobDbContext = jobScope.ServiceProvider.GetRequiredService<ITestDbContext>();
                using var jobContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

                var loadedPost = await jobDbContext.Posts.FindOneAsync(post.Id);

                // Assert.
                //job1 doesn't see job0's in-memory mutation, nor its changed models
                Assert.Equal("content", loadedPost.Content);
                Assert.Empty(jobDbContext.ChangedModelsList);

                job1Verified.SetResult();
            });

            await Task.WhenAll(job0, job1);
        }

        [Fact]
        public async Task SaveChangesOnOneContextDoesntPersistOthers()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("title", "content");
            var note = new Note("text");
            await testDbContext.Posts.CreateAsync(post);
            await secondDbContext.Notes.CreateAsync(note);

            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedPost = await testDbContext.Posts.FindOneAsync(post.Id);
            var loadedNote = await secondDbContext.Notes.FindOneAsync(note.Id);

            loadedPost.Content = "updated content";
            loadedNote.Text = "updated text";

            // Action.
            await testDbContext.SaveChangesAsync();

            // Assert.
            //read the db state through a fresh scope, not deduplicated with local instances
            using (var readScope = fixture.ServiceProvider.CreateScope())
            {
                var readTestDbContext = readScope.ServiceProvider.GetRequiredService<ITestDbContext>();
                var readSecondDbContext = readScope.ServiceProvider.GetRequiredService<ISecondDbContext>();
                using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

                var foundPost = await readTestDbContext.Posts.FindOneAsync(post.Id);
                var foundNote = await readSecondDbContext.Notes.FindOneAsync(note.Id);
                Assert.Equal("updated content", foundPost.Content);
                Assert.Equal("text", foundNote.Text);
            }

            // Action.
            await secondDbContext.SaveChangesAsync();

            // Assert.
            using (var readScope = fixture.ServiceProvider.CreateScope())
            {
                var readSecondDbContext = readScope.ServiceProvider.GetRequiredService<ISecondDbContext>();
                using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

                var foundNote = await readSecondDbContext.Notes.FindOneAsync(note.Id);
                Assert.Equal("updated text", foundNote.Text);
            }
        }
    }
}
