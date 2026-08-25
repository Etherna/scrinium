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
using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.Extensions;
using Etherna.Scrinium.Core.Serialization;
using Etherna.Scrinium.Core.Serialization.Serializers;
using Etherna.Scrinium.IntegrationTests.Models;

namespace Etherna.Scrinium.IntegrationTests.ModelMaps
{
    internal sealed class CatalogMap : IModelMapsCollector
    {
        public void Register(IDbContextEngine dbContextEngine)
        {
            dbContextEngine.MapRegistry.AddModelMap<Catalog>(
                "9d2c5b1e-7a48-4f3b-a6d0-2e8c4b7f9a15",
                mm =>
                {
                    mm.AutoMap();

                    // Set members with custom serializers.
                    mm.SetMemberSerializer(c => c.IndexedPosts,
                        new DictionarySerializer<string, Post>(
                            DictionaryRepresentation.ArrayOfDocuments,
                            new StringSerializer(),
                            PostMap.MinimalReferenceSerializer(dbContextEngine)));
                    mm.SetMemberSerializer(c => c.LabeledPosts,
                        new DictionarySerializer<string, Post>(
                            DictionaryRepresentation.Document,
                            new StringSerializer(),
                            PostMap.MinimalReferenceSerializer(dbContextEngine)));
                });
        }
    }
}
