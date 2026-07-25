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
using Etherna.MongODM.Core.Serialization;
using Etherna.MongODM.Core.Serialization.Serializers;
using Etherna.MongODM.IntegrationTests.Models;

namespace Etherna.MongODM.IntegrationTests.ModelMaps
{
    internal sealed class AccountMap : IModelMapsCollector
    {
        public void Register(IDbContextEngine dbContextEngine)
        {
            dbContextEngine.MapRegistry.AddModelMap<AccountBase>("6f3a2b10-83a0-4b00-9c00-000000000001");
            dbContextEngine.MapRegistry.AddModelMap<Web2Account>("6f3a2b10-83a0-4b00-9c00-000000000002");
            dbContextEngine.MapRegistry.AddModelMap<Web3Account>("6f3a2b10-83a0-4b00-9c00-000000000003");
        }

        /// <summary>
        /// Minimal reference to the account entity
        /// </summary>
        public static ReferenceSerializer<AccountBase, string> MinimalReferenceSerializer(IDbContextEngine dbContextEngine) =>
            ReferenceSerializer.Create(dbContextEngine, config =>
            {
                config.AddModelMap<ModelBase>("74c04e83-59f4-4b64-b661-6ac207f5ee1e");
                config.AddModelMap<EntityModelBase<string>>("13b9a0d5-fa42-42bf-a1ea-8d0f728a2b95", mm =>
                {
                    mm.MapIdMember(m => m.Id);
                    mm.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId));
                });
                config.AddModelMap<AccountBase>("07f213bb-fdb8-4f0f-88c9-c67e0d8a8639", _ => { });
                config.AddModelMap<Web2Account>("30963bcc-e11a-4966-ba80-be24824cf3f5", _ => { });
                config.AddModelMap<Web3Account>("cd753cd7-7b31-401b-a0da-4db08ad418ad", _ => { });
            },
            sourceRepository: (ITestDbContext dbContext) => dbContext.Accounts);

        /// <summary>
        /// Summary information serializer, including the account username
        /// </summary>
        public static ReferenceSerializer<AccountBase, string> SummaryInfoSerializer(IDbContextEngine dbContextEngine) =>
            ReferenceSerializer.Create(dbContextEngine, config =>
            {
                config.AddModelMap<ModelBase>("3aef741c-a3a2-4c37-a1ba-caf35cf1de13");
                config.AddModelMap<EntityModelBase<string>>("f9c15751-018f-47eb-9564-84076c8d6b3f", mm =>
                {
                    mm.MapIdMember(m => m.Id);
                    mm.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId));
                });
                config.AddModelMap<AccountBase>("ea361dc0-6a25-42bf-bd39-de2a758c8e59", mm =>
                {
                    mm.MapMember(m => m.Username);
                });
                config.AddModelMap<Web2Account>("f5825985-4d3a-43e0-a15a-e6f504c34e07", _ => { });
                config.AddModelMap<Web3Account>("06d4e4c1-1e57-4bd0-a071-90fe7d3dbc2a", _ => { });
            },
            sourceRepository: (ITestDbContext dbContext) => dbContext.Accounts);
    }
}
