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

using Etherna.MongoDB.Bson.Serialization;
using Etherna.Scrinium.AspNetCore.UI.Areas.Scrinium.Pages;
using Etherna.Scrinium.AspNetCore.UI.Auth.Filters;
using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Extensions;
using Etherna.Scrinium.Core.Options;
using Etherna.Scrinium.Core.Repositories;
using Etherna.Scrinium.Core.Serialization.Mapping;
using Etherna.Scrinium.Core.Serialization.Serializers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.AspNetCore.UI
{
    public class DashboardDocumentStructureTest
    {
        // Internal classes.
        private sealed class AllowAllAuthFilter : IDashboardAuthFilter
        {
            public Task<bool> AuthorizeAsync(HttpContext? context) => Task.FromResult(true);
        }
        public class AuthorModel : EntityModelBase
        {
            public virtual string? Email { get; set; }
            public virtual string? Name { get; set; }
        }
        public class ColorTagModel : TagModel
        {
            public virtual string? Color { get; set; }
        }
        /* The id lives on the abstract base of the models, mapped by its own model map: every
         * schema of a derived type inherits its members, and the schemas of the base types
         * shape a partial document, never written on its own. */
        public abstract class EntityModelBase : IEntityModel<string>
        {
            public virtual IDictionary<string, object>? ExtraElements { get; protected set; }
            public virtual string Id { get; set; } = null!;
        }
        public class MetadataModel
        {
            public virtual string? Language { get; set; }
        }
        public class PostModel : EntityModelBase
        {
            public virtual AuthorModel? Author { get; set; }
            public virtual MetadataModel? Metadata { get; set; }
            public virtual IEnumerable<AuthorModel>? Reviewers { get; set; }
            public virtual IEnumerable<TagModel>? Tags { get; set; }
            public virtual string? Title { get; set; }
        }
        public abstract class TagModel
        {
            public virtual string? Label { get; set; }
            public virtual TagModel? Related { get; set; }
        }

        // Consts.
        private const string PagePath = "/Scrinium";

        // Fields.
        private readonly Mock<IDbContextEngine> engineMock = new();
        private readonly MapRegistry mapRegistry = new();

        // Constructor.
        public DashboardDocumentStructureTest()
        {
            engineMock.Setup(engine => engine.DbContextType)
                .Returns(typeof(IDbContext));
            engineMock.Setup(engine => engine.DiscriminatorRegistry)
                .Returns(new Mock<IDiscriminatorRegistry>().Object);
            engineMock.Setup(engine => engine.Identifier)
                .Returns("TestDbContext");
            engineMock.Setup(engine => engine.MapRegistry)
                .Returns(mapRegistry);
            engineMock.Setup(engine => engine.Options)
                .Returns(new DbContextOptions());
            engineMock.Setup(engine => engine.SerializerRegistry)
                .Returns(new BsonSerializerRegistry());

            mapRegistry.Initialize(engineMock.Object, new Mock<ILogger>().Object);
        }

        // Tests.
        [Fact]
        public void ShapesCloseTheSubDocumentsReachingASchemaAlreadyExpanded()
        {
            /* A model graph cycle (a model nesting itself through the shapes of its derived
             * types, or a summary denormalizing a reference to its own model) reaches a schema
             * already open on the exploration path: the shape closes there reporting the cycle,
             * and naming the shape it repeats, instead of nesting without end. */

            // Setup.
            RegisterModelMaps();
            var repositories = BuildRepositories(("authors", typeof(AuthorModel)), ("posts", typeof(PostModel)));

            // Action.
            var shape = IndexModel.GetDocumentShapes(repositories["posts"]).Single();

            // Assert.
            var tagShape = Assert.Single(shape.Elements
                .Single(element => element.ElementName == nameof(PostModel.Tags)).Shapes);
            Assert.False(tagShape.IsCycle);

            var cycleShape = Assert.Single(tagShape.Elements
                .Single(element => element.ElementName == nameof(TagModel.Related)).Shapes);
            Assert.True(cycleShape.IsCycle);
            Assert.Equal(nameof(ColorTagModel), cycleShape.ModelTypeName);
            Assert.Equal("colorTagSchemaId", cycleShape.SchemaId);
            Assert.Empty(cycleShape.Elements);
        }

        [Fact]
        public void ShapesExpandEmbeddedModelsWithoutTheReferenceFlag()
        {
            /* Only entity models are referenced: an embedded model is written whole into the
             * document, and its members are not the summary of anything. */

            // Setup.
            RegisterModelMaps();
            var repositories = BuildRepositories(("authors", typeof(AuthorModel)), ("posts", typeof(PostModel)));

            // Action.
            var shape = IndexModel.GetDocumentShapes(repositories["posts"]).Single();

            // Assert.
            var metadataElement = shape.Elements.Single(element =>
                element.ElementName == nameof(PostModel.Metadata));
            Assert.False(metadataElement.IsReference);
            var metadataShape = Assert.Single(metadataElement.Shapes);
            Assert.Equal(nameof(MetadataModel), metadataShape.ModelTypeName);
            Assert.Equal([nameof(MetadataModel.Language)], metadataShape.Elements.Select(element => element.ElementName));
        }

        [Fact]
        public void ShapesExpandPolymorphicSubDocumentsWithTheirConcreteTypes()
        {
            /* A sub-document is written by the concrete type the member receives, whatever the
             * declared one: the shapes of an element are the registered ones of every concrete
             * model type assignable to it, so a member declaring an abstract type still tells
             * how its documents look. */

            // Setup.
            RegisterModelMaps();
            var repositories = BuildRepositories(("authors", typeof(AuthorModel)), ("posts", typeof(PostModel)));

            // Action.
            var shape = IndexModel.GetDocumentShapes(repositories["posts"]).Single();

            // Assert.
            var tagsElement = shape.Elements.Single(element => element.ElementName == nameof(PostModel.Tags));
            Assert.False(tagsElement.IsReference);
            Assert.Equal("[]", tagsElement.ContainerSuffix);
            Assert.Equal(nameof(TagModel), tagsElement.TypeName);
            var tagShape = Assert.Single(tagsElement.Shapes);
            Assert.Equal(nameof(ColorTagModel), tagShape.ModelTypeName);
            Assert.Equal(
                [nameof(TagModel.Label), nameof(TagModel.Related), nameof(ColorTagModel.Color)],
                tagShape.Elements.Select(element => element.ElementName));
        }

        [Fact]
        public void ShapesExpandReferencesWithTheirDenormalizedMembers()
        {
            // Setup.
            RegisterModelMaps();
            var repositories = BuildRepositories(("authors", typeof(AuthorModel)), ("posts", typeof(PostModel)));

            // Action.
            var shape = IndexModel.GetDocumentShapes(repositories["posts"]).Single();

            // Assert.
            //the denormalizing reference: its members are carried by the referencing document
            var authorElement = shape.Elements.Single(element => element.ElementName == nameof(PostModel.Author));
            Assert.True(authorElement.IsReference);
            Assert.True(authorElement.HasReferencedModelRepository);
            Assert.True(authorElement.IsUpdatePropagated);
            Assert.Empty(authorElement.ContainerSuffix);
            var authorShape = Assert.Single(authorElement.Shapes);
            Assert.Equal(nameof(AuthorModel), authorShape.ModelTypeName);
            Assert.Equal("authorRefSchemaId", authorShape.SchemaId);
            Assert.Equal(
                ["_id", nameof(AuthorModel.Name)],
                authorShape.Elements.Select(element => element.ElementName));

            //the id only reference of a collection member: an array of summaries
            var reviewersElement = shape.Elements.Single(element => element.ElementName == nameof(PostModel.Reviewers));
            Assert.True(reviewersElement.IsReference);
            Assert.Equal("[]", reviewersElement.ContainerSuffix);
            Assert.Equal(["_id"], Assert.Single(reviewersElement.Shapes).Elements.Select(element => element.ElementName));
        }

        [Fact]
        public void ShapesReportEveryRegisteredSchemaWithItsWrittenElements()
        {
            /* Each registered schema shapes the documents written while it was the active one:
             * the deprecated ones keep their shape until a migration rewrites them. The extra
             * elements bag stays out, collecting the elements no member maps. */

            // Setup.
            RegisterModelMaps();
            var repositories = BuildRepositories(("authors", typeof(AuthorModel)), ("posts", typeof(PostModel)));

            // Action.
            var shapes = IndexModel.GetDocumentShapes(repositories["authors"]).ToArray();

            // Assert.
            Assert.Equal(
                [("authorSchemaId", true), ("previousAuthorSchemaId", false)],
                shapes.Select(shape => (shape.SchemaId, shape.IsActiveSchema)));
            Assert.Equal(
                ["_id", nameof(AuthorModel.Email), nameof(AuthorModel.Name)],
                shapes[0].Elements.Select(element => element.ElementName));
            Assert.Equal(
                ["_id", nameof(AuthorModel.Name)],
                shapes[1].Elements.Select(element => element.ElementName));
            Assert.Equal("String", shapes[0].Elements.Last().TypeName);
        }

        [Fact]
        public void ShapesReportTheReferencedModelsWithoutARepository()
        {
            /* MODM-101: a reference sourced on another db context is saved there, and the
             * dependencies update propagation stays per engine: its summaries are never
             * rewritten by this db context. */

            // Setup.
            RegisterModelMaps();
            var repositories = BuildRepositories(("posts", typeof(PostModel)));

            // Action.
            var shape = IndexModel.GetDocumentShapes(repositories["posts"]).Single();

            // Assert.
            var authorElement = shape.Elements.Single(element => element.ElementName == nameof(PostModel.Author));
            Assert.True(authorElement.IsReference);
            Assert.False(authorElement.HasReferencedModelRepository);
        }

        [Fact]
        public async Task PageRendersTheDocumentStructuresSection()
        {
            // Setup.
            RegisterModelMaps();
            var repositories = BuildRepositories(("authors", typeof(AuthorModel)), ("posts", typeof(PostModel)));
            using var host = await StartDashboardHostAsync(repositories["posts"].DbContext);

            // Action.
            var response = await host.GetTestClient().GetAsync(new Uri(PagePath, UriKind.Relative));

            // Assert.
            response.EnsureSuccessStatusCode();
            var page = await response.Content.ReadAsStringAsync();
            Assert.Contains("Document structures", page, StringComparison.Ordinal);
            Assert.Contains("data-repository=\"posts\"", page, StringComparison.Ordinal);
            Assert.Contains("data-schema-id=\"postSchemaId\"", page, StringComparison.Ordinal);
            //the referenced summary is expanded inside the document structure
            Assert.Contains("authorRefSchemaId", page, StringComparison.Ordinal);
            //the structure repeating one already expanded reports the cycle closing it
            Assert.Contains("shape-tag cycle", page, StringComparison.Ordinal);
        }

        // Helpers.
        private ReferenceSerializer<AuthorModel, string> AuthorIdOnlyReferenceSerializer() =>
            new(engineMock.Object, config =>
            {
                config.AddModelMap<EntityModelBase>("idOnlyBaseRefSchemaId");
                config.AddModelMap<AuthorModel>("idOnlyAuthorRefSchemaId", _ => { });
            });

        private ReferenceSerializer<AuthorModel, string> AuthorReferenceSerializer() =>
            new(engineMock.Object, config =>
            {
                config.AddModelMap<EntityModelBase>("baseRefSchemaId");
                config.AddModelMap<AuthorModel>(
                    "authorRefSchemaId",
                    classMap => classMap.MapMember(author => author.Name));
            });

        private Dictionary<string, IRepository> BuildRepositories(params (string Name, Type ModelType)[] repositories)
        {
            var dbContextMock = new Mock<IDbContext>();
            dbContextMock.Setup(dbContext => dbContext.Engine)
                .Returns(engineMock.Object);

            var repositoryMocks = repositories.ToDictionary(
                repository => repository.Name,
                repository =>
                {
                    var repositoryMock = new Mock<IRepository>();
                    repositoryMock.Setup(repo => repo.DbContext).Returns(dbContextMock.Object);
                    repositoryMock.Setup(repo => repo.ModelType).Returns(repository.ModelType);
                    repositoryMock.Setup(repo => repo.Name).Returns(repository.Name);
                    return repositoryMock.Object;
                });

            var repositoryRegistryMock = new Mock<IRepositoryRegistry>();
            repositoryRegistryMock.Setup(registry => registry.Repositories)
                .Returns(repositoryMocks.Values.ToArray());
            dbContextMock.Setup(dbContext => dbContext.RepositoryRegistry)
                .Returns(repositoryRegistryMock.Object);

            return repositoryMocks;
        }

        private void RegisterModelMaps()
        {
            mapRegistry.AddModelMap<ColorTagModel>("colorTagSchemaId");
            mapRegistry.AddModelMap<EntityModelBase>("entityBaseSchemaId");
            mapRegistry.AddModelMap<MetadataModel>("metadataSchemaId");
            mapRegistry.AddModelMap<TagModel>("tagSchemaId", classMap =>
            {
                classMap.AutoMap();
                classMap.SetMemberSerializer(
                    tag => tag.Related!,
                    new MappedSerializerAdapter<TagModel>(engineMock.Object));
            });
            mapRegistry.AddModelMap<AuthorModel>("authorSchemaId")
                .AddSecondarySchema("previousAuthorSchemaId", classMap =>
                    classMap.MapMember(author => author.Name));
            mapRegistry.AddModelMap<PostModel>("postSchemaId", classMap =>
            {
                classMap.AutoMap();
                classMap.SetMemberSerializer(post => post.Author!, AuthorReferenceSerializer());
                classMap.SetMemberSerializer(
                    post => post.Metadata!,
                    new MappedSerializerAdapter<MetadataModel>(engineMock.Object));
                classMap.SetMemberSerializer(
                    post => post.Reviewers!,
                    new EnumerableSerializer<AuthorModel>(AuthorIdOnlyReferenceSerializer()));
                classMap.SetMemberSerializer(
                    post => post.Tags!,
                    new EnumerableSerializer<TagModel>(new MappedSerializerAdapter<TagModel>(engineMock.Object)));
            });
            mapRegistry.Freeze();
        }

        private static async Task<IHost> StartDashboardHostAsync(IDbContext dbContext) =>
            await new HostBuilder()
                .ConfigureWebHost(webHostBuilder => webHostBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        var scriniumOptions = new ScriniumOptions();
                        ((IScriniumOptionsBuilder)scriniumOptions).SetDbContextTypes([typeof(IDbContext)]);

                        services.AddRazorPages()
                            .AddApplicationPart(typeof(IndexModel).Assembly);
                        services.AddHttpContextAccessor();
                        services.AddScriniumAdminDashboard(new DashboardOptions
                        {
                            AuthFilters = [new AllowAllAuthFilter()]
                        });
                        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(scriniumOptions));
                        services.AddSingleton(dbContext);
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints => endpoints.MapRazorPages());
                    }))
                .StartAsync();
    }
}
