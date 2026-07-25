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
using Etherna.MongODM.Core.ProxyModels;
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
    public class PreloadTests : IDisposable
    {
        // Fields.
        private readonly ITestDbContext dbContext;
        private readonly IntegrationFixture fixture;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public PreloadTests(IntegrationFixture fixture)
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
        public async Task BatchPreloadUpgradesTheSummaryReferences()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var firstPost = new Post("first title", "content");
            var secondPost = new Post("second title", "content");
            await dbContext.Posts.CreateAsync(firstPost);
            await dbContext.Posts.CreateAsync(secondPost);

            var blog = new Blog("blog");
            blog.AddPost(firstPost);
            blog.AddPost(secondPost);
            await dbContext.Blogs.CreateAsync(blog);

            // Action.
            //load on a new scope: the referenced posts are Id only summaries
            using var readScope = fixture.ServiceProvider.CreateScope();
            var readDbContext = readScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            var loadedBlog = await readDbContext.Blogs.FindOneAsync(blog.Id);
            var loadedPosts = loadedBlog.Posts.ToArray();
            var firstLoadedPost = loadedPosts.Single(p => p.Id == firstPost.Id);
            var secondLoadedPost = loadedPosts.Single(p => p.Id == secondPost.Id);

            /* Both references are summaries, but the second post is also the blog LastPost,
             * serialized with the preview reference: its Title merged into the shared
             * instance through the identity map, and is already loaded. */
            Assert.True(((IReferenceable)firstLoadedPost).IsSummary);
            Assert.True(((IReferenceable)secondLoadedPost).IsSummary);
            Assert.False(readDbContext.IsMemberLoaded(firstLoadedPost, p => p.Title));
            Assert.True(readDbContext.IsMemberLoaded(secondLoadedPost, p => p.Title));

            await readDbContext.LoadValuesAsync(loadedPosts, p => p.Title);

            // Assert.
            //only the summary missing the member loaded full; the other stayed untouched
            Assert.False(((IReferenceable)firstLoadedPost).IsSummary);
            Assert.True(((IReferenceable)secondLoadedPost).IsSummary);
            Assert.True(readDbContext.IsMemberLoaded(firstLoadedPost, p => p.Title));
            Assert.Equal("first title", firstLoadedPost.Title);
            Assert.Equal("second title", secondLoadedPost.Title);
        }

        [Fact]
        public async Task PreloadOfLoadedMembersIsANoOp()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("title", "content");
            await dbContext.Posts.CreateAsync(post);
            var blog = new Blog("blog");
            blog.AddPost(post);
            await dbContext.Blogs.CreateAsync(blog);

            // Action.
            //the reference summary already carries the id: ensuring it must not load anything
            using var readScope = fixture.ServiceProvider.CreateScope();
            var readDbContext = readScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            var loadedBlog = await readDbContext.Blogs.FindOneAsync(blog.Id);
            var loadedPost = loadedBlog.Posts.Single();

            await readDbContext.LoadValuesAsync(loadedPost, p => p.Id);

            // Assert.
            Assert.True(((IReferenceable)loadedPost).IsSummary);
            Assert.True(readDbContext.IsMemberLoaded(loadedPost, p => p.Id));
        }
    }
}
