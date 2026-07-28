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
using Etherna.MongODM.Core.Serialization;
using Etherna.MongODM.Core.Serialization.Serializers;
using Etherna.MongODM.IntegrationTests.Models;

namespace Etherna.MongODM.IntegrationTests.ModelMaps
{
    internal sealed class ItemMap : IModelMapsCollector
    {
        public void Register(IDbContextEngine dbContextEngine)
        {
            dbContextEngine.MapRegistry.AddModelMap<Item>(
                "c51eb7d3-0f34-4766-9a9a-4f959abdc647",
                mm =>
                {
                    mm.AutoMap();

                    // Set members with custom serializers.
                    mm.SetMemberSerializer(i => i.Reviews,
                        new EnumerableSerializer<Review>(
                            ReviewMap.SummaryInfoSerializer(dbContextEngine)));
                });
        }

        /// <summary>
        /// Minimal reference to the entity
        /// </summary>
        public static ReferenceSerializer<Item, string> MinimalReferenceSerializer(IDbContextEngine dbContextEngine) =>
            ReferenceSerializer.Create(dbContextEngine, config =>
            {
                config.AddModelMap<ModelBase>("28cf04b3-8593-42a6-ac8c-199c89721cff");
                config.AddModelMap<EntityModelBase<string>>("3b30c7ce-c8c7-4c15-8e0a-0edd05b77573", mm =>
                {
                    mm.MapIdMember(m => m.Id);
                    mm.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId));
                });
                config.AddModelMap<Item>("2cd49405-a9be-4f4c-9bc5-1113a29f6f31", _ => { });
            },
            sourceRepository: (ITestDbContext dbContext) => dbContext.Items);
    }
}
