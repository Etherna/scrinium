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
using Etherna.MongODM.Core;
using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.Extensions;
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.Core.Serialization;
using Etherna.MongODM.Core.Serialization.Serializers;
using Etherna.MongODM.IntegrationTests.Models;
using System.Collections.Generic;

namespace Etherna.MongODM.IntegrationTests
{
    /// <summary>
    /// A misconfigured db context: a Post reference serializer without a declared source,
    /// on a db context without any repository compatible with Post. Initialization must
    /// fail fast, pointing to the cross db context declaration.
    /// </summary>
    internal sealed class InvalidMissingSourceDbContext : DbContext
    {
        // Properties.
        //repositories
        public IRepository<Blog, string> Blogs { get; } = new Repository<Blog, string>("invalidMissingSourceBlogs");

        // Protected properties.
        protected override IEnumerable<IModelMapsCollector> ModelMapsCollectors =>
            [new InvalidMissingSourceBlogMap()];

        // Helpers.
        private sealed class InvalidMissingSourceBlogMap : IModelMapsCollector
        {
            public void Register(IDbContextEngine dbContextEngine)
            {
                dbContextEngine.MapRegistry.AddModelMap<Blog>(
                    "5b755f60-4d1c-4a89-b3f2-2e6da13c81de",
                    mm =>
                    {
                        mm.AutoMap();

                        //implicit source on a model type without any compatible repository: invalid configuration
                        mm.SetMemberSerializer(b => b.LastPost!, new ReferenceSerializer<Post, string>(
                            dbContextEngine,
                            config =>
                            {
                                config.AddModelMap<ModelBase>("8f0e12bd-6317-42a2-a56f-6a97f0e10978");
                                config.AddModelMap<EntityModelBase<string>>("ff5e91b4-994f-4258-a231-3f4ffb69f5b0", mm2 =>
                                {
                                    mm2.MapIdMember(m => m.Id);
                                    mm2.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId));
                                });
                                config.AddModelMap<Post>("39c53e70-2450-4c9b-b31e-11c1b2babe64", _ => { });
                            }));
                        mm.SetMemberSerializer(b => b.Posts, new EnumerableSerializer<Post>(
                            new ReferenceSerializer<Post, string>(
                                dbContextEngine,
                                config =>
                                {
                                    config.AddModelMap<ModelBase>("d0084448-92f5-4022-b8ae-4bbb0b0f71c2");
                                    config.AddModelMap<EntityModelBase<string>>("e3d5b7a1-9c44-4b6f-9f10-9b71c33d1f5a", mm2 =>
                                    {
                                        mm2.MapIdMember(m => m.Id);
                                        mm2.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId));
                                    });
                                    config.AddModelMap<Post>("6a5cb9d5-3103-4899-a53b-73d9a4a5320c", _ => { });
                                })));
                    });
            }
        }
    }
}
