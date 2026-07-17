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

using Etherna.MongoDB.Bson;
using Etherna.MongoDB.Driver;
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
    public class UpdateDocDependenciesTests : IDisposable
    {
        // Fields.
        private readonly ITestDbContext dbContext;
        private readonly IntegrationFixture fixture;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public UpdateDocDependenciesTests(IntegrationFixture fixture)
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
        public async Task ChangedReferencedModelUpdatesSummariesOnDependentDocuments()
        {
            /* Pin the whole summary maintenance flow: replacing a referenced model enqueues an
             * update task from DbMaintainer.OnUpdatedModel, carrying the repository that
             * performed the write. Its execution refreshes the denormalized sub-documents
             * embedded by dependent documents. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            fixture.TaskRunner.ClearPending();

            var post = new Post("original title", "content");
            await dbContext.Posts.CreateAsync(post);

            var blog = new Blog("my blog");
            blog.AddPost(post);
            await dbContext.Blogs.CreateAsync(blog);

            // Action: update the referenced post through its repository.
            var loadedPost = await dbContext.Posts.FindOneAsync(post.Id);
            loadedPost.Title = "updated title";
            await dbContext.SaveChangesAsync();

            // Assert: the persisted summary is refreshed only by the enqueued task execution.
            var blogsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("blogs");
            var blogFilter = Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(blog.Id));

            var rawBlog = await blogsCollection.Find(blogFilter).SingleAsync();
            Assert.Equal("original title", rawBlog["LastPost"]["Title"].AsString);

            await fixture.TaskRunner.ExecutePendingAsync(fixture.ServiceProvider);

            rawBlog = await blogsCollection.Find(blogFilter).SingleAsync();
            Assert.Equal("updated title", rawBlog["LastPost"]["Title"].AsString);
        }
    }
}
