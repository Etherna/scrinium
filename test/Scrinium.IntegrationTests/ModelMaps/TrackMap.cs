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
using Etherna.Scrinium.Core.Options;
using Etherna.Scrinium.Core.Serialization;
using Etherna.Scrinium.Core.Serialization.Serializers;
using Etherna.Scrinium.IntegrationTests.Models;

namespace Etherna.Scrinium.IntegrationTests.ModelMaps
{
    internal sealed class TrackMap : IModelMapsCollector
    {
        public void Register(IDbContextEngine dbContextEngine)
        {
            dbContextEngine.MapRegistry.AddModelMap<Track>(
                "1f7c3a58-9b2e-4d06-8a4f-5c1d9e0b7a26");
        }

        /// <summary>
        /// Preview information serializer, without a declared origin delete policy: a track
        /// deleted through its repository removes this reference by default
        /// </summary>
        public static ReferenceSerializer<Track, string> PreviewInfoSerializer(IDbContextEngine dbContextEngine) =>
            new(dbContextEngine, config =>
            {
                config.AddModelMap<ModelBase>("64b7f0c2-8e15-4a39-9d6c-2f8a1b5e0d47");
                config.AddModelMap<EntityModelBase<string>>("d90a5c31-7f68-4e24-b1a9-8c3e6d2f5b70", mm =>
                {
                    mm.MapIdMember(m => m.Id);
                    mm.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId));
                });
                config.AddModelMap<Track>("3e8d1b76-0a49-4c52-9f38-6b7d4a0c2e91", mm =>
                {
                    mm.MapMember(m => m.Title);
                });
            });

        /// <summary>
        /// Reference to the track entity, declaring to keep the reference when the track is
        /// deleted through its repository: the explicit opt-out of the delete propagation
        /// </summary>
        public static ReferenceSerializer<Track, string> KeptReferenceSerializer(IDbContextEngine dbContextEngine) =>
            new(dbContextEngine, config =>
            {
                config.OriginDelete = OriginDeleteMode.KeepReference;
                config.AddModelMap<ModelBase>("f81d3c26-5a90-4e74-b2c8-0e6f9a1d4b57");
                config.AddModelMap<EntityModelBase<string>>("2c95e7f0-8d41-4a63-9b27-5f0c8e3a6d19", mm =>
                {
                    mm.MapIdMember(m => m.Id);
                    mm.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId));
                });
                config.AddModelMap<Track>("8f04a9d3-6b52-4c81-a7e0-3d9b5f2c8e46", _ => { });
            });

        /// <summary>
        /// Reference to the track entity, declaring the referencing document delete when the
        /// track is deleted through its repository
        /// </summary>
        public static ReferenceSerializer<Track, string> CascadeDeleteReferenceSerializer(IDbContextEngine dbContextEngine) =>
            new(dbContextEngine, config =>
            {
                config.OriginDelete = OriginDeleteMode.DeleteReferencingDocument;
                config.AddModelMap<ModelBase>("b25e8d40-1c73-4f96-a08b-9e6f3c1d7a54");
                config.AddModelMap<EntityModelBase<string>>("7a4f9e12-5d80-4b37-8c6a-0d2b5f9e3c68", mm =>
                {
                    mm.MapIdMember(m => m.Id);
                    mm.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId));
                });
                config.AddModelMap<Track>("592c6f83-4e17-4a90-b5d2-1f8e0a7c4b36", _ => { });
            });
    }
}
