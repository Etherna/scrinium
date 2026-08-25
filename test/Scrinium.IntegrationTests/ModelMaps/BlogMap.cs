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
using Etherna.Scrinium.Core.Serialization;
using Etherna.Scrinium.Core.Serialization.Serializers;
using Etherna.Scrinium.IntegrationTests.Models;

namespace Etherna.Scrinium.IntegrationTests.ModelMaps
{
    internal sealed class BlogMap : IModelMapsCollector
    {
        public void Register(IDbContextEngine dbContextEngine)
        {
            dbContextEngine.MapRegistry.AddModelMap<Blog>(
                "4cd47c3a-0495-4724-a954-c8c64ba8d6e2",
                mm =>
                {
                    mm.AutoMap();

                    // Set members with custom serializers.
                    mm.SetMemberSerializer(b => b.LastPost!, PostMap.PreviewInfoSerializer(dbContextEngine));
                    mm.SetMemberSerializer(b => b.Posts,
                        new EnumerableSerializer<Post>(
                            PostMap.MinimalReferenceSerializer(dbContextEngine)));
                });
        }

        /// <summary>
        /// Preview information serializer, including the blog title and the nested
        /// last post reference
        /// </summary>
        public static ReferenceSerializer<Blog, string> PreviewWithLastPostSerializer(IDbContextEngine dbContextEngine) =>
            ReferenceSerializer.Create(dbContextEngine, config =>
            {
                config.AddModelMap<ModelBase>("2f8a9b3c-6d1e-4f7a-9c2b-5e8d0a4b7c1f");
                config.AddModelMap<EntityModelBase<string>>("b4e7d2a9-3c5f-48b1-a6d8-9f0e2c7b5a3d", mm =>
                {
                    mm.MapIdMember(m => m.Id);
                    mm.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId));
                });
                config.AddModelMap<Blog>("9c1d4e7f-2b8a-4c6d-b3e9-7a5f0d8c2b4e", mm =>
                {
                    mm.MapMember(b => b.LastPost).SetSerializer(PostMap.PreviewInfoSerializer(dbContextEngine));
                    mm.MapMember(b => b.Title);
                });
            },
            sourceRepository: (ITestDbContext dbContext) => dbContext.Blogs);
    }
}
