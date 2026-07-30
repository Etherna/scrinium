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
    /// A misconfigured db context: a Post reference serializer declares its typed source
    /// repository on ITestDbContext, not implemented by this db context. Initialization
    /// must fail fast.
    /// </summary>
    internal sealed class InvalidTypedSourceDbContext : DbContext
    {
        // Properties.
        //repositories
        public IRepository<Blog, string> Blogs { get; } = new Repository<Blog, string>("invalidTypedSourceBlogs");
        public IRepository<Post, string> Posts { get; } = new Repository<Post, string>("invalidTypedSourcePosts");

        // Protected properties.
        protected override IEnumerable<IModelMapsCollector> ModelMapsCollectors =>
            [new InvalidTypedSourceBlogMap()];

        // Helpers.
        private sealed class InvalidTypedSourceBlogMap : IModelMapsCollector
        {
            public void Register(IDbContextEngine dbContextEngine)
            {
                dbContextEngine.MapRegistry.AddModelMap<Post>(
                    "97a62fe8-b50e-4e73-ade6-c4414c48ac73");

                dbContextEngine.MapRegistry.AddModelMap<Blog>(
                    "96713991-0375-4c83-a8d6-2d5b16e3bfb4",
                    mm =>
                    {
                        mm.AutoMap();

                        //typed source declared on a db context type not implemented here: invalid configuration
                        mm.SetMemberSerializer(b => b.LastPost!, ReferenceSerializer.Create(
                            dbContextEngine,
                            config =>
                            {
                                config.AddModelMap<ModelBase>("1db768de-69c8-40e6-8401-ae668e273862");
                                config.AddModelMap<EntityModelBase<string>>("02b94bdc-7d09-4680-b523-13c0b45c4441", mm2 =>
                                {
                                    mm2.MapIdMember(m => m.Id);
                                    mm2.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId));
                                });
                                config.AddModelMap<Post>("b4805f08-e6d2-4d51-9a5f-e7d748c83139", _ => { });
                            },
                            sourceRepository: (ITestDbContext dbContext) => dbContext.Posts));
                        mm.SetMemberSerializer(b => b.Posts, new EnumerableSerializer<Post>(
                            new ReferenceSerializer<Post, string>(
                                dbContextEngine,
                                config =>
                                {
                                    config.AddModelMap<ModelBase>("93957d93-477a-44ac-bdd2-7078413d599f");
                                    config.AddModelMap<EntityModelBase<string>>("04c45b9e-b699-4b11-933d-0578374bbd82", mm2 =>
                                    {
                                        mm2.MapIdMember(m => m.Id);
                                        mm2.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId));
                                    });
                                    config.AddModelMap<Post>("fc42a27b-7709-4fe0-88a8-c7911b581747", _ => { });
                                })));
                    });
            }
        }
    }
}
