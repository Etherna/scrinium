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
using System.Threading.Tasks;
using Xunit;

namespace Etherna.MongODM.IntegrationTests
{
    [Collection("Integration")]
    public class UpdateDocDependenciesTests(IntegrationFixture fixture)
    {
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
            await fixture.TestDbContext.Posts.CreateAsync(post);

            var blog = new Blog("my blog");
            blog.AddPost(post);
            await fixture.TestDbContext.Blogs.CreateAsync(blog);

            // Action: update the referenced post through its repository.
            var loadedPost = await fixture.TestDbContext.Posts.FindOneAsync(post.Id);
            loadedPost.Title = "updated title";
            await fixture.TestDbContext.SaveChangesAsync();

            // Assert: the persisted summary is refreshed only by the enqueued task execution.
            var blogsCollection = fixture.TestDbContext.Engine.Database.GetCollection<BsonDocument>("blogs");
            var blogFilter = Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(blog.Id));

            var rawBlog = await blogsCollection.Find(blogFilter).SingleAsync();
            Assert.Equal("original title", rawBlog["LastPost"]["Title"].AsString);

            await fixture.TaskRunner.ExecutePendingAsync(fixture.ServiceProvider);

            rawBlog = await blogsCollection.Find(blogFilter).SingleAsync();
            Assert.Equal("updated title", rawBlog["LastPost"]["Title"].AsString);
        }
    }
}
