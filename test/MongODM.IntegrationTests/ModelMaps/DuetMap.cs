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
    internal sealed class DuetMap : IModelMapsCollector
    {
        public void Register(IDbContextEngine dbContextEngine)
        {
            dbContextEngine.MapRegistry.AddModelMap<Duet>(
                "5c2e8a17-9f40-4b63-8d05-1a7c4e9b2f58",
                mm =>
                {
                    mm.AutoMap();

                    // Set members with custom serializers.
                    mm.SetMemberSerializer(m => m.Partner!, PartnerReferenceSerializer(dbContextEngine));
                });
        }

        /// <summary>
        /// Reference to the partner duet, declaring the referencing document delete when the
        /// partner is deleted through its repository: two duets partnering each other close
        /// the cascade cycle on the documents already deleted
        /// </summary>
        private static ReferenceSerializer<Duet, string> PartnerReferenceSerializer(IDbContextEngine dbContextEngine) =>
            new(dbContextEngine, config =>
            {
                config.OriginDelete = OriginDeleteMode.DeleteReferencingDocument;
                config.AddModelMap<ModelBase>("0a9d5e42-7c81-4f36-b2d8-6e1f9c4a7b03");
                config.AddModelMap<EntityModelBase<string>>("e64b1f80-3a57-4d29-9c06-8f2e5b0d4a71", mm =>
                {
                    mm.MapIdMember(m => m.Id);
                    mm.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId));
                });
                config.AddModelMap<Duet>("93f7c0a5-2e68-4b14-a9d3-5c8e1f6b0d42", _ => { });
            });
    }
}
