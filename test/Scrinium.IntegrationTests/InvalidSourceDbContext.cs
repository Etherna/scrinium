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
    /// A misconfigured db context: a Post reference serializer declares the Blogs
    /// repository as source, which can't host Post documents. Initialization must fail fast.
    /// </summary>
    internal sealed class InvalidSourceDbContext : DbContext
    {
        // Properties.
        //repositories
        public IRepository<Blog, string> Blogs { get; } = new Repository<Blog, string>("invalidSourceBlogs");
        public IRepository<Post, string> Posts { get; } = new Repository<Post, string>("invalidSourcePosts");

        // Protected properties.
        protected override IEnumerable<IModelMapsCollector> ModelMapsCollectors =>
            [new InvalidSourceBlogMap()];

        // Helpers.
        private sealed class InvalidSourceBlogMap : IModelMapsCollector
        {
            public void Register(IDbContextEngine dbContextEngine)
            {
                dbContextEngine.MapRegistry.AddModelMap<Post>(
                    "d6ea45a4-c476-4a2f-9f1c-3ab8f0b09660");

                dbContextEngine.MapRegistry.AddModelMap<Blog>(
                    "5b7e2ec3-964f-42c8-83c2-71c2ac6dae76",
                    mm =>
                    {
                        mm.AutoMap();

                        /* Declared source repository handling an incompatible model type:
                         * invalid configuration, expressible only with the untyped
                         * constructor selector (the typed factory rejects it at compile time). */
                        mm.SetMemberSerializer(b => b.LastPost!, new ReferenceSerializer<Post, string>(
                            dbContextEngine,
                            config =>
                            {
                                config.AddModelMap<ModelBase>("b6ba1eb5-e0e9-4c8f-9494-8e01023c4924");
                                config.AddModelMap<EntityModelBase<string>>("c33fce62-6c25-4a95-8a95-1ec1f3fc9e58", mm2 =>
                                {
                                    mm2.MapIdMember(m => m.Id);
                                    mm2.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId));
                                });
                                config.AddModelMap<Post>("00d1361e-b76c-4467-b652-92c66c63be04", _ => { });
                            },
                            sourceRepository: dbContext => ((InvalidSourceDbContext)dbContext).Blogs));
                        mm.SetMemberSerializer(b => b.Posts, new EnumerableSerializer<Post>(
                            new ReferenceSerializer<Post, string>(
                                dbContextEngine,
                                config =>
                                {
                                    config.AddModelMap<ModelBase>("1a3ac8f6-821b-45d9-8204-873aaa474e23");
                                    config.AddModelMap<EntityModelBase<string>>("311602d5-9d25-4fa3-ab8c-550639975cff", mm2 =>
                                    {
                                        mm2.MapIdMember(m => m.Id);
                                        mm2.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId));
                                    });
                                    config.AddModelMap<Post>("bac00ba7-c3b7-4d55-afb7-86cf257ea854", _ => { });
                                })));
                    });
            }
        }
    }
}
