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
using Etherna.MongODM.Core;
using Etherna.MongODM.Core.Exceptions;
using Etherna.MongODM.Core.Options;
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
    public class OriginRepositoriesTests : IDisposable
    {
        // Fields.
        private readonly ITestDbContext dbContext;
        private readonly IntegrationFixture fixture;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public OriginRepositoriesTests(IntegrationFixture fixture)
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
        public void AmbiguousModelTypeResolutionThrows()
        {
            /* Two repositories handle Post on this db context: resolving the repository
             * from the model type alone is a configuration error, failing fast. */
            Assert.Throws<MongodmAmbiguousRepositoryException>(
                () => dbContext.RepositoryRegistry.GetRepositoryByHandledModelType(typeof(Post)));
        }

        [Fact]
        public void IncompatibleSourceRepositoryFailsAtInitialization()
        {
            /* A reference serializer declaring a source repository that can't host its
             * model type is a configuration error: it must fail fast at engine
             * initialization, detailing the incompatibility. */

            // Setup.
            var dbContext = new InvalidSourceDbContext();
            var dependencies = fixture.ServiceProvider.GetRequiredService<IDbDependencies>();
            var options = new DbContextOptions
            {
                ConnectionString = $"{fixture.MongoDbUrl}/mongodm-it-invalid-source"
            };

            // Action & assert.
            var exception = Assert.Throws<MongodmInvalidEntityTypeException>(
                () => dbContext.BuildEngine(dependencies, new MongoClient(fixture.MongoDbUrl), options));
            Assert.Contains(nameof(InvalidSourceDbContext), exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(Post), exception.Message, StringComparison.Ordinal);
            Assert.Contains("invalidSourceBlogs", exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(Blog), exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void MismatchedSourceDbContextTypeFailsAtInitialization()
        {
            /* A reference serializer declaring its typed source repository on a db context
             * type neither implemented by the hosting db context nor declared as its child
             * db context type is a configuration error: it must fail fast at engine
             * initialization, detailing the unreachable type and the missing declaration. */

            // Setup.
            var dbContext = new InvalidTypedSourceDbContext();
            var dependencies = fixture.ServiceProvider.GetRequiredService<IDbDependencies>();
            var options = new DbContextOptions
            {
                ConnectionString = $"{fixture.MongoDbUrl}/mongodm-it-invalid-typed-source"
            };

            // Action & assert.
            var exception = Assert.Throws<InvalidOperationException>(
                () => dbContext.BuildEngine(dependencies, new MongoClient(fixture.MongoDbUrl), options));
            Assert.Contains(nameof(Post), exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(ITestDbContext), exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(InvalidTypedSourceDbContext), exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(DbContextOptions.ParentFor), exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AmbiguousCrossSourceChildDbContextTypesFailAtInitialization()
        {
            /* A cross db context source declared on a db context type implemented by
             * multiple declared child db context types can't identify its host: it must
             * fail fast at engine initialization, detailing the ambiguous types. */

            // Setup.
            var dbContext = new ParentDbContext();
            var dependencies = fixture.ServiceProvider.GetRequiredService<IDbDependencies>();
            var options = new DbContextOptions
            {
                ConnectionString = $"{fixture.MongoDbUrl}/mongodm-it-ambiguous-cross-source"
            };
            options.ParentFor<IFirstNotesDbContext>();
            options.ParentFor<ISecondNotesDbContext>();

            // Action & assert.
            var exception = Assert.Throws<InvalidOperationException>(
                () => dbContext.BuildEngine(dependencies, new MongoClient(fixture.MongoDbUrl), options));
            Assert.Contains(nameof(Note), exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(ISecondDbContext), exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(IFirstNotesDbContext), exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(ISecondNotesDbContext), exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ReachableCrossSourceChildDbContextTypeBuildsEngine()
        {
            /* A cross db context source declared on a db context type declared as child
             * db context type is a valid configuration: the engine builds, deferring the
             * repository resolution to the child instances attached at each scope. */

            // Setup.
            var dbContext = new ParentDbContext();
            var dependencies = fixture.ServiceProvider.GetRequiredService<IDbDependencies>();
            var options = new DbContextOptions
            {
                ConnectionString = $"{fixture.MongoDbUrl}/mongodm-it-reachable-cross-source"
            };
            options.ParentFor<ISecondDbContext>();

            // Action.
            var engine = dbContext.BuildEngine(dependencies, new MongoClient(fixture.MongoDbUrl), options);

            // Assert.
            Assert.NotNull(engine);
        }

        [Fact]
        public async Task IdentityMapKeysByRepository()
        {
            /* The same document id on two collections identifies two different documents:
             * the loaded models deduplicate per repository, not per model type. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("title", "content");
            await dbContext.Posts.CreateAsync(post);

            //copy the raw document into the archived collection, with the same id
            var postsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("posts");
            var archivedCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("archivedPosts");
            var rawPost = await postsCollection.Find(
                Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(post.Id))).SingleAsync();
            await archivedCollection.InsertOneAsync(rawPost);

            // Action.
            var loadedPost = await dbContext.Posts.FindOneAsync(post.Id);
            var loadedArchivedPost = await dbContext.ArchivedPosts.FindOneAsync(post.Id);

            // Assert.
            Assert.NotSame(loadedPost, loadedArchivedPost);
            Assert.Same(dbContext.Posts, ((IReferenceable)loadedPost).SourceRepository);
            Assert.Same(dbContext.ArchivedPosts, ((IReferenceable)loadedArchivedPost).SourceRepository);

            //read through returns each canonical instance from its own repository
            Assert.Same(loadedPost, await dbContext.Posts.FindOneAsync(post.Id));
            Assert.Same(loadedArchivedPost, await dbContext.ArchivedPosts.FindOneAsync(post.Id));
        }

        [Fact]
        public void ImplicitReferenceWithoutCompatibleRepositoryFailsAtInitialization()
        {
            /* A reference serializer without a declared source, on a model type without any
             * compatible repository on its db context, is a configuration error: it must
             * fail fast at engine initialization, pointing to the cross db context
             * declaration. Every reference of a built engine binds a source repository. */

            // Setup.
            var dbContext = new InvalidMissingSourceDbContext();
            var dependencies = fixture.ServiceProvider.GetRequiredService<IDbDependencies>();
            var options = new DbContextOptions
            {
                ConnectionString = $"{fixture.MongoDbUrl}/mongodm-it-invalid-missing-source"
            };

            // Action & assert.
            var exception = Assert.Throws<InvalidOperationException>(
                () => dbContext.BuildEngine(dependencies, new MongoClient(fixture.MongoDbUrl), options));
            Assert.Contains(nameof(InvalidMissingSourceDbContext), exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(Post), exception.Message, StringComparison.Ordinal);
            Assert.Contains("Create", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ImplicitAmbiguousReferenceFailsAtInitialization()
        {
            /* A reference serializer without sourceRepository, on a model type handled by
             * two repositories of its db context, is a configuration error: it must fail
             * fast at engine initialization, detailing the involved repositories. */

            // Setup.
            var dbContext = new InvalidDbContext();
            var dependencies = fixture.ServiceProvider.GetRequiredService<IDbDependencies>();
            var options = new DbContextOptions
            {
                ConnectionString = $"{fixture.MongoDbUrl}/mongodm-it-invalid"
            };

            // Action & assert.
            var exception = Assert.Throws<MongodmAmbiguousRepositoryException>(
                () => dbContext.BuildEngine(dependencies, new MongoClient(fixture.MongoDbUrl), options));
            Assert.Contains(nameof(InvalidDbContext), exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(Post), exception.Message, StringComparison.Ordinal);
            Assert.Contains("ArchivedPosts", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Posts", exception.Message, StringComparison.Ordinal);
            Assert.Contains("sourceRepository", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ImplicitReferenceBindsResolvedSourceRepository()
        {
            /* Reference serializers without a declared source resolve it at engine build,
             * from the single compatible repository property of their db context: the
             * reference proxies bind it like a declared one. */

            // Setup.
            using var implicitScope = fixture.ServiceProvider.CreateScope();
            var implicitDbContext = implicitScope.ServiceProvider.GetRequiredService<IImplicitSourceDbContext>();
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            var post = new Post("post title", "post content");
            await implicitDbContext.Posts.CreateAsync(post);
            var blog = new Blog("blog title");
            blog.AddPost(post);
            await implicitDbContext.Blogs.CreateAsync(blog);

            // Action.
            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedBlog = await implicitDbContext.Blogs.FindOneAsync(blog.Id);
            var referencedPost = loadedBlog.LastPost!;

            // Assert.
            Assert.Same(implicitDbContext.Posts, ((IReferenceable)referencedPost).SourceRepository);
            Assert.Same(referencedPost, loadedBlog.Posts.Single());

            //lazy loading works through the resolved source repository
            Assert.Equal("post content", referencedPost.Content);
        }

        [Fact]
        public async Task ReferenceBindsConfiguredOriginRepository()
        {
            /* Post references are configured with the Posts origin repository on the model
             * maps: the reference proxies bind to it, even if the Post model type alone
             * would be ambiguous on this db context. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("post title", "post content");
            await dbContext.Posts.CreateAsync(post);
            var blog = new Blog("blog title");
            blog.AddPost(post);
            await dbContext.Blogs.CreateAsync(blog);

            // Action.
            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var loadedBlog = await dbContext.Blogs.FindOneAsync(blog.Id);
            var referencedPost = loadedBlog.LastPost!;

            // Assert.
            Assert.Same(dbContext.Posts, ((IReferenceable)referencedPost).SourceRepository);

            //lazy loading works through the configured origin repository
            Assert.Equal("post content", referencedPost.Content);
        }

        [Fact]
        public async Task SameModelTypeLivesOnTwoCollections()
        {
            /* Two repositories of the same db context manage the same model type on two
             * different collections: models load, track and save each on its own origin. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var post = new Post("post title", "post content");
            var archivedPost = new Post("archived title", "archived content");
            await dbContext.Posts.CreateAsync(post);
            await dbContext.ArchivedPosts.CreateAsync(archivedPost);

            // Action.
            var loadedPost = await dbContext.Posts.FindOneAsync(post.Id);
            var loadedArchivedPost = await dbContext.ArchivedPosts.FindOneAsync(archivedPost.Id);
            loadedPost.Content = "updated content";
            loadedArchivedPost.Content = "updated archived content";
            await dbContext.SaveChangesAsync();

            // Assert.
            //each collection hosts only its own documents, with its own changes persisted
            using var readScope = fixture.ServiceProvider.CreateScope();
            var readDbContext = readScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            var foundPost = await readDbContext.Posts.FindOneAsync(post.Id);
            var foundArchivedPost = await readDbContext.ArchivedPosts.FindOneAsync(archivedPost.Id);
            Assert.Equal("updated content", foundPost.Content);
            Assert.Equal("updated archived content", foundArchivedPost.Content);

            Assert.Null(await readDbContext.Posts.TryFindOneAsync(archivedPost.Id));
            Assert.Null(await readDbContext.ArchivedPosts.TryFindOneAsync(post.Id));
        }

        // Nested types.
        /* Marker child db context types for the cross source ambiguity validation: both
         * implement ISecondDbContext, so a source declared on it can't identify its host. */
        private interface IFirstNotesDbContext : ISecondDbContext { }
        private interface ISecondNotesDbContext : ISecondDbContext { }
    }
}
