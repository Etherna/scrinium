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
    internal sealed class JournalMap : IModelMapsCollector
    {
        public void Register(IDbContextEngine dbContextEngine)
        {
            dbContextEngine.MapRegistry.AddModelMap<Journal>(
                "7c7e0dc3-1f38-46a2-9138-b9db3a533b70",
                mm =>
                {
                    mm.AutoMap();

                    // Set members with custom serializers.
                    mm.SetMemberSerializer(m => m.PinnedNote!, NoteReferenceSerializer(dbContextEngine));
                });
        }

        /// <summary>
        /// Reference to the note entity of the child db context, with its tag denormalized
        /// </summary>
        public static ReferenceSerializer<Note, string> NoteReferenceSerializer(IDbContextEngine dbContextEngine) =>
            ReferenceSerializer.Create(dbContextEngine, config =>
            {
                config.AddModelMap<ModelBase>("d3af7b01-3e02-4bbc-8b74-a56b2c1e8393");
                config.AddModelMap<EntityModelBase<string>>("57339cc4-0a5c-42f9-a615-33a969e19c31", mm =>
                {
                    mm.MapIdMember(m => m.Id);
                    mm.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId));
                });
                config.AddModelMap<Note>("a86b8949-3c6f-45a4-a0d9-a3080cbd3c14", mm =>
                {
                    mm.MapMember(m => m.Tag);
                });
            },
            sourceRepository: (ISecondDbContext dbContext) => dbContext.Notes);
    }
}
