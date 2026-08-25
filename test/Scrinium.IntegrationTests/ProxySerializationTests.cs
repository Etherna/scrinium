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
    public class ProxySerializationTests : IDisposable
    {
        // Fields.
        private readonly ITestDbContext dbContext;
        private readonly IntegrationFixture fixture;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public ProxySerializationTests(IntegrationFixture fixture)
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
        public async Task SerializingADerivedProxyMatchesThePlainInstanceDocument()
        {
            /* MODM-189: proxy types have no registered model maps, and a proxy instance
             * serializes through the class map of its purged concrete type. Replacing an
             * untouched loaded model (a proxy of the derived type, read from the base
             * typed repository) must rewrite a document identical to the created one:
             * same members, same schema id, and the derived type discriminator -
             * never the proxy type name. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var account = new Web2Account("username");
            await dbContext.Accounts.CreateAsync(account);

            var accountsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("accounts");
            var accountFilter = Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(account.Id));
            var createdDocument = await accountsCollection.Find(accountFilter).SingleAsync();

            // Action.
            //load on a new scope: the instance is a proxy of the derived type
            using var replaceScope = fixture.ServiceProvider.CreateScope();
            var replaceDbContext = replaceScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var replaceContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            var loadedAccount = await replaceDbContext.Accounts.FindOneAsync(account.Id);
            Assert.True(replaceDbContext.Engine.ProxyGenerator.IsProxyType(loadedAccount.GetType()));
            Assert.IsAssignableFrom<Web2Account>(loadedAccount);

            await replaceDbContext.Accounts.ReplaceAsync(loadedAccount);

            // Assert.
            var replacedDocument = await accountsCollection.Find(accountFilter).SingleAsync();
            Assert.Equal(createdDocument, replacedDocument);
            Assert.DoesNotContain(loadedAccount.GetType().Name, replacedDocument["_t"].ToString(), StringComparison.Ordinal);
            Assert.Contains(nameof(Web2Account), replacedDocument["_t"].ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task SerializingAProxyReferenceMatchesThePlainReferenceDocument()
        {
            /* MODM-189: reference serializer configurations too have no proxy model maps: a
             * reference member valued with a loaded (proxy) instance writes the same reference
             * document written by a plain instance - the summary members of the reference
             * schema, with the reference schema id. */

            // Setup.
            //blog referencing the plain created post
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("title", "content");
            await dbContext.Posts.CreateAsync(post);

            var plainBlog = new Blog("blog");
            plainBlog.AddPost(post);
            await dbContext.Blogs.CreateAsync(plainBlog);

            // Action.
            //blog referencing the loaded (proxy) post, on a new scope
            using var proxyScope = fixture.ServiceProvider.CreateScope();
            var proxyDbContext = proxyScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var proxyContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            var loadedPost = await proxyDbContext.Posts.FindOneAsync(post.Id);
            Assert.True(proxyDbContext.Engine.ProxyGenerator.IsProxyType(loadedPost.GetType()));

            var proxyBlog = new Blog("blog");
            proxyBlog.AddPost(loadedPost);
            await proxyDbContext.Blogs.CreateAsync(proxyBlog);

            // Assert.
            var blogsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("blogs");
            var plainBlogDocument = await blogsCollection.Find(
                Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(plainBlog.Id))).SingleAsync();
            var proxyBlogDocument = await blogsCollection.Find(
                Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(proxyBlog.Id))).SingleAsync();

            Assert.Equal(plainBlogDocument["Posts"], proxyBlogDocument["Posts"]);
            Assert.Equal(plainBlogDocument["LastPost"], proxyBlogDocument["LastPost"]);
        }
    }
}
