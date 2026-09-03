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
using Etherna.MongoDB.Bson.Serialization.IdGenerators;
using Etherna.MongoDB.Bson.Serialization.Serializers;
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Serialization;

namespace Etherna.Scrinium.Core.Domain.ModelMaps
{
    internal sealed class ModelBaseMap : IModelMapsCollector
    {
        public void Register(IDbContextEngine dbContextEngine)
        {
            // Register class maps.
            dbContextEngine.MapRegistry.AddModelMap<ModelBase>("bff55d53-0517-4a93-8fda-7bd448181449");

            dbContextEngine.MapRegistry.AddModelMap<EntityModelBase<string>>("586b48f5-ba1f-45e3-a812-744f88c1c969",
                schema =>
                {
                    schema.AutoMap();

                    // Set Id representation.
                    schema.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId))
                                      .SetIdGenerator(new StringObjectIdGenerator());
                });
        }
    }
}
