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
using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Extensions;
using Etherna.Scrinium.Core.Serialization;
using Etherna.Scrinium.Core.Serialization.Serializers;
using Etherna.Scrinium.IntegrationTests.Models;

namespace Etherna.Scrinium.IntegrationTests.ModelMaps
{
    internal sealed class DigestMap : IModelMapsCollector
    {
        public void Register(IDbContextEngine dbContextEngine)
        {
            dbContextEngine.MapRegistry.AddModelMap<Digest>(
                "088cbc9f-3e56-4607-8126-96b8d2e72d3d",
                mm =>
                {
                    mm.AutoMap();

                    // Set members with custom serializers.
                    mm.SetMemberSerializer(m => m.PinnedNote, NoteReferenceSerializer(dbContextEngine));
                });
        }

        /// <summary>
        /// Reference to the note entity, with its tag denormalized
        /// </summary>
        private static ReferenceSerializer<Note, string> NoteReferenceSerializer(IDbContextEngine dbContextEngine) =>
            new(dbContextEngine, config =>
            {
                config.AddModelMap<ModelBase>("6208a951-d1f3-409e-954f-a2e80e68720f");
                config.AddModelMap<EntityModelBase<string>>("2ac49765-0244-4776-8674-ede1722daf4d", mm =>
                {
                    mm.MapIdMember(m => m.Id);
                    mm.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId));
                });
                config.AddModelMap<Note>("ece03420-624c-4752-946f-450fdfc6cae2", mm =>
                {
                    mm.MapMember(m => m.Tag);
                });
            });
    }
}
