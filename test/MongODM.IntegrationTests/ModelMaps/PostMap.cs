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
using Etherna.MongODM.Core.Serialization;
using Etherna.MongODM.Core.Serialization.Serializers;
using Etherna.MongODM.IntegrationTests.Models;

namespace Etherna.MongODM.IntegrationTests.ModelMaps
{
    internal sealed class PostMap : IModelMapsCollector
    {
        public void Register(IDbContextEngine dbContextEngine)
        {
            dbContextEngine.MapRegistry.AddModelMap<Post>(
                "f44d03bb-dd75-496b-82fb-27d571be602b");
        }

        /// <summary>
        /// Preview information serializer, including the post title
        /// </summary>
        public static ReferenceSerializer<Post, string> PreviewInfoSerializer(IDbContextEngine dbContextEngine) =>
            new(dbContextEngine, config =>
            {
                config.AddModelMap<ModelBase>("5a55693d-e49a-4079-968d-0d210db49721");
                config.AddModelMap<EntityModelBase<string>>("3e87ebac-b5a4-4372-9b44-07cec75d5c24", mm =>
                {
                    mm.MapIdMember(m => m.Id);
                    mm.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId));
                });
                config.AddModelMap<Post>("8fa8f258-70b2-464f-8b57-11de27ca0b81", mm =>
                {
                    mm.MapMember(m => m.Title);
                });
            },
            sourceRepository: dbContext => ((ITestDbContext)dbContext).Posts);

        /// <summary>
        /// Minimal reference to the entity
        /// </summary>
        public static ReferenceSerializer<Post, string> ReferenceSerializer(IDbContextEngine dbContextEngine) =>
            new(dbContextEngine, config =>
            {
                config.AddModelMap<ModelBase>("837dd14f-022c-459b-9c84-c4cd0bf5aea6");
                config.AddModelMap<EntityModelBase<string>>("473640d8-3259-43dd-baa2-effebfdf8ef7", mm =>
                {
                    mm.MapIdMember(m => m.Id);
                    mm.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId));
                });
                config.AddModelMap<Post>("e7d1fe44-c5d7-4e5b-8ab6-898295619131", _ => { });
            },
            sourceRepository: dbContext => ((ITestDbContext)dbContext).Posts);
    }
}
