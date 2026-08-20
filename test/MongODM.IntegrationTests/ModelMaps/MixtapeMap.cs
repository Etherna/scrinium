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

using Etherna.MongoDB.Bson.Serialization.Options;
using Etherna.MongoDB.Bson.Serialization.Serializers;
using Etherna.MongODM.Core;
using Etherna.MongODM.Core.Extensions;
using Etherna.MongODM.Core.Serialization;
using Etherna.MongODM.Core.Serialization.Serializers;
using Etherna.MongODM.IntegrationTests.Models;

namespace Etherna.MongODM.IntegrationTests.ModelMaps
{
    internal sealed class MixtapeMap : IModelMapsCollector
    {
        public void Register(IDbContextEngine dbContextEngine)
        {
            dbContextEngine.MapRegistry.AddModelMap<Mixtape>(
                "c8f2a6d1-3b90-4e57-8d24-7a5c0e9b1f63",
                mm =>
                {
                    mm.AutoMap();

                    // Set members with custom serializers.
                    mm.SetMemberSerializer(m => m.Highlight!, TrackMap.PreviewInfoSerializer(dbContextEngine));
                    mm.SetMemberSerializer(m => m.IndexedTracks,
                        new DictionarySerializer<string, Track>(
                            DictionaryRepresentation.ArrayOfDocuments,
                            new StringSerializer(),
                            TrackMap.PreviewInfoSerializer(dbContextEngine)));
                    mm.SetMemberSerializer(m => m.LabeledTracks,
                        new DictionarySerializer<string, Track>(
                            DictionaryRepresentation.Document,
                            new StringSerializer(),
                            TrackMap.PreviewInfoSerializer(dbContextEngine)));
                    mm.SetMemberSerializer(m => m.Pinned!, TrackMap.KeptReferenceSerializer(dbContextEngine));
                    mm.SetMemberSerializer(m => m.Tracks,
                        new EnumerableSerializer<Track>(
                            TrackMap.PreviewInfoSerializer(dbContextEngine)));
                });
        }
    }
}
