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
using Etherna.MongoDB.Bson.Serialization.IdGenerators;
using Etherna.MongoDB.Bson.Serialization.Serializers;
using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.Serialization;

namespace Etherna.Scrinium.AspNetCoreSample.Models.ModelMaps
{
    class ModelBaseMap : IModelMapsCollector
    {
        public void Register(IDbContextEngine dbContextEngine)
        {
            dbContextEngine.MapRegistry.AddModelMap<ModelBase>("1252861f-82d9-4c72-975e-3571d5e1b6e6");

            dbContextEngine.MapRegistry.AddModelMap<EntityModelBase<string>>("81dd8b35-a0af-44d9-80b4-ab7ae9844eb5", schema =>
            {
                schema.AutoMap();

                schema.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId))
                                    .SetIdGenerator(new StringObjectIdGenerator());
            });
        }
    }
}
