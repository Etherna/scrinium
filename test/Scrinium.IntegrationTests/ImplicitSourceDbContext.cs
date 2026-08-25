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
using Etherna.MongoDB.Bson.Serialization.Serializers;
using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Extensions;
using Etherna.Scrinium.Core.Repositories;
using Etherna.Scrinium.Core.Serialization;
using Etherna.Scrinium.Core.Serialization.Serializers;
using Etherna.Scrinium.IntegrationTests.Models;
using System.Collections.Generic;

namespace Etherna.Scrinium.IntegrationTests
{
    /// <summary>
    /// A db context with reference serializers NOT declaring their source repository:
    /// sources resolve at maps freeze from the single compatible repository property.
    /// </summary>
    public interface IImplicitSourceDbContext : IDbContext
    {
        IRepository<Blog, string> Blogs { get; }
        IRepository<Post, string> Posts { get; }
    }

    internal sealed class ImplicitSourceDbContext : DbContext, IImplicitSourceDbContext
    {
        // Properties.
        //repositories
        public IRepository<Blog, string> Blogs { get; } = new Repository<Blog, string>("implicitBlogs");
        public IRepository<Post, string> Posts { get; } = new Repository<Post, string>("implicitPosts");

        // Protected properties.
        protected override IEnumerable<IModelMapsCollector> ModelMapsCollectors =>
            [new ImplicitBlogMap(), new ImplicitPostMap()];

        // Helpers.
        private sealed class ImplicitBlogMap : IModelMapsCollector
        {
            public void Register(IDbContextEngine dbContextEngine)
            {
                dbContextEngine.MapRegistry.AddModelMap<Blog>(
                    "e92377b7-986d-4f79-8ab9-3ec0451d5d76",
                    mm =>
                    {
                        mm.AutoMap();

                        //no sourceRepository: resolved at maps freeze
                        mm.SetMemberSerializer(b => b.LastPost!, PreviewInfoSerializer(dbContextEngine));
                        mm.SetMemberSerializer(b => b.Posts,
                            new EnumerableSerializer<Post>(ReferenceSerializer(dbContextEngine)));
                    });
            }

            private static ReferenceSerializer<Post, string> PreviewInfoSerializer(IDbContextEngine dbContextEngine) =>
                new(dbContextEngine, config =>
                {
                    config.AddModelMap<ModelBase>("13a49491-96fc-44ef-a8d5-9c15c217a961");
                    config.AddModelMap<EntityModelBase<string>>("eb8185bd-c7a3-4a01-b643-14066a9eb6ed", mm =>
                    {
                        mm.MapIdMember(m => m.Id);
                        mm.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId));
                    });
                    config.AddModelMap<Post>("5671ad4d-4224-470b-8af5-4751276fa078", mm =>
                    {
                        mm.MapMember(m => m.Title);
                    });
                });

            private static ReferenceSerializer<Post, string> ReferenceSerializer(IDbContextEngine dbContextEngine) =>
                new(dbContextEngine, config =>
                {
                    config.AddModelMap<ModelBase>("8d41a9d6-5a21-4c97-9ef9-7bc4e8b5c8d9");
                    config.AddModelMap<EntityModelBase<string>>("c2f07baa-c51e-40cc-b8b1-bb56a15a6396", mm =>
                    {
                        mm.MapIdMember(m => m.Id);
                        mm.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId));
                    });
                    config.AddModelMap<Post>("d4409eb3-fefa-4901-a076-e7b5e7f29913", _ => { });
                });
        }

        private sealed class ImplicitPostMap : IModelMapsCollector
        {
            public void Register(IDbContextEngine dbContextEngine)
            {
                dbContextEngine.MapRegistry.AddModelMap<Post>(
                    "91e2af83-f04a-4b20-841c-ab5b80acf8d0");
            }
        }
    }
}
