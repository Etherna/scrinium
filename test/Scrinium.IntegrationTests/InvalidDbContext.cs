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
    /// A misconfigured db context: two repositories handle Post, but a Post reference
    /// serializer doesn't force its origin repository. Initialization must fail fast.
    /// </summary>
    internal sealed class InvalidDbContext : DbContext
    {
        // Properties.
        //repositories
        public IRepository<Post, string> ArchivedPosts { get; } = new Repository<Post, string>("invalidArchivedPosts");
        public IRepository<Blog, string> Blogs { get; } = new Repository<Blog, string>("invalidBlogs");
        public IRepository<Post, string> Posts { get; } = new Repository<Post, string>("invalidPosts");

        // Protected properties.
        protected override IEnumerable<IModelMapsCollector> ModelMapsCollectors =>
            [new InvalidBlogMap()];

        // Helpers.
        private sealed class InvalidBlogMap : IModelMapsCollector
        {
            public void Register(IDbContextEngine dbContextEngine)
            {
                dbContextEngine.MapRegistry.AddModelMap<Post>(
                    "afda7297-66ad-4282-ae6e-4568f818c854");

                dbContextEngine.MapRegistry.AddModelMap<Blog>(
                    "07c1961b-cd2f-4d1d-9820-4dd873629ffb",
                    mm =>
                    {
                        mm.AutoMap();

                        //no sourceRepository on an ambiguous model type: invalid configuration
                        mm.SetMemberSerializer(b => b.LastPost!, new ReferenceSerializer<Post, string>(
                            dbContextEngine,
                            config =>
                            {
                                config.AddModelMap<ModelBase>("bd4b78a5-2e33-4d1b-a95e-16e1a9dcbfeb");
                                config.AddModelMap<EntityModelBase<string>>("07a52a87-1f30-42ac-a271-e83ba2b04188", mm2 =>
                                {
                                    mm2.MapIdMember(m => m.Id);
                                    mm2.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId));
                                });
                                config.AddModelMap<Post>("94c9ff9d-2751-4be2-89dd-d6bf1e0bcc9c", _ => { });
                            }));
                        mm.SetMemberSerializer(b => b.Posts, new EnumerableSerializer<Post>(
                            new ReferenceSerializer<Post, string>(
                                dbContextEngine,
                                config =>
                                {
                                    config.AddModelMap<ModelBase>("a01f89f8-e661-44b7-b2e0-670a107eb9e4");
                                    config.AddModelMap<EntityModelBase<string>>("14a226c8-4c61-419a-9086-d556283aa1c5", mm2 =>
                                    {
                                        mm2.MapIdMember(m => m.Id);
                                        mm2.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId));
                                    });
                                    config.AddModelMap<Post>("52aa9e88-cbe4-4d4e-b0f0-4a76ee4c25b0", _ => { });
                                })));
                    });
            }
        }
    }
}
