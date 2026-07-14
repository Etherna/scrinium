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
using System.Threading.Tasks;
using Xunit;

namespace Etherna.MongODM.IntegrationTests
{
    [Collection("Integration")]
    public class ScopeIsolationTests(IntegrationFixture fixture)
    {
        // Fields.
        private readonly ISecondDbContext secondDbContext = fixture.SecondDbContext;
        private readonly ITestDbContext testDbContext = fixture.TestDbContext;

        // Tests.
        [Fact]
        public async Task ParallelScopesDontShareTrackedModels()
        {
            /* Simulate two background jobs on the same singleton db context, each with its
             * own execution scope, like Hangfire jobs run with AsyncLocalContextHangfireFilter. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("title", "content");
            await testDbContext.Posts.CreateAsync(post);

            var job0Mutated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var job1Verified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            // Action.
            var job0 = Task.Run(async () =>
            {
                using var jobContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

                var loadedPost = await testDbContext.Posts.FindOneAsync(post.Id);
                loadedPost.Content = "updated by job0";

                Assert.Single(testDbContext.ChangedModelsList);

                job0Mutated.SetResult();
                await job1Verified.Task;
            });

            var job1 = Task.Run(async () =>
            {
                await job0Mutated.Task;

                using var jobContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

                var loadedPost = await testDbContext.Posts.FindOneAsync(post.Id);

                // Assert.
                //job1 doesn't see job0's in-memory mutation, nor its tracked models
                Assert.Equal("content", loadedPost.Content);
                Assert.Empty(testDbContext.ChangedModelsList);

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
            using (var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext())
            {
                var foundPost = await testDbContext.Posts.FindOneAsync(post.Id);
                var foundNote = await secondDbContext.Notes.FindOneAsync(note.Id);
                Assert.Equal("updated content", foundPost.Content);
                Assert.Equal("text", foundNote.Text);
            }

            // Action.
            await secondDbContext.SaveChangesAsync();

            // Assert.
            using (var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext())
            {
                var foundNote = await secondDbContext.Notes.FindOneAsync(note.Id);
                Assert.Equal("updated text", foundNote.Text);
            }
        }
    }
}
