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
using Etherna.MongODM.Core.Options;
using Etherna.MongODM.Core.Serialization;
using Etherna.MongODM.Core.Serialization.Serializers;
using Etherna.MongODM.IntegrationTests.Models;

namespace Etherna.MongODM.IntegrationTests.ModelMaps
{
    internal sealed class RoyaltyMap : IModelMapsCollector
    {
        public void Register(IDbContextEngine dbContextEngine)
        {
            dbContextEngine.MapRegistry.AddModelMap<Royalty>(
                "0d6b4f27-8a51-4c93-b7e0-3f9a2c5d8e14",
                mm =>
                {
                    mm.AutoMap();

                    // Set members with custom serializers.
                    mm.SetMemberSerializer(m => m.Subject!, TrackMap.CascadeDeleteReferenceSerializer(dbContextEngine));
                });
        }

        /// <summary>
        /// Reference to the royalty entity, declaring the referencing document delete when
        /// the royalty is deleted through its repository: chains the cascade of its track
        /// </summary>
        public static ReferenceSerializer<Royalty, string> CascadeDeleteReferenceSerializer(IDbContextEngine dbContextEngine) =>
            new(dbContextEngine, config =>
            {
                config.OriginDelete = OriginDeleteMode.DeleteReferencingDocument;
                config.AddModelMap<ModelBase>("e17c9b64-2d80-4f35-a6c1-8b0d5e3f7a92");
                config.AddModelMap<EntityModelBase<string>>("48a0d5f9-6e23-4b71-9c58-2d7f0b4e6a35", mm =>
                {
                    mm.MapIdMember(m => m.Id);
                    mm.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId));
                });
                config.AddModelMap<Royalty>("a63f2e90-7b48-4d16-8f0a-5c9e1d3b7f28", _ => { });
            });
    }
}
