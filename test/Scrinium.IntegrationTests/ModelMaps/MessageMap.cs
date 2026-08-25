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

using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.Extensions;
using Etherna.Scrinium.Core.Serialization;
using Etherna.Scrinium.Core.Serialization.Serializers;
using Etherna.Scrinium.IntegrationTests.Models;

namespace Etherna.Scrinium.IntegrationTests.ModelMaps
{
    internal sealed class MessageMap : IModelMapsCollector
    {
        public void Register(IDbContextEngine dbContextEngine)
        {
            dbContextEngine.MapRegistry.AddModelMap<Dispatch>("c9dc5a5d-54ed-475c-8262-7a748e233226");

            dbContextEngine.MapRegistry.AddModelMap<Envelope>(
                "86d458e2-e618-466f-b29c-bf04fceb5196",
                mm =>
                {
                    mm.AutoMap();

                    // Set members with custom serializers.
                    mm.SetMemberSerializer(m => m.Recipients,
                        new EnumerableSerializer<AccountBase>(
                            AccountMap.SummaryInfoSerializer(dbContextEngine)));
                });

            dbContextEngine.MapRegistry.AddModelMap<Message>(
                "d2a3f7e1-4b8c-49a1-9f6e-3f52290ab1c4",
                mm =>
                {
                    mm.AutoMap();

                    // Set members with custom serializers.
                    mm.SetMemberSerializer(m => m.Author, AccountMap.SummaryInfoSerializer(dbContextEngine));
                    mm.SetMemberSerializer(m => m.Editor, AccountMap.MinimalReferenceSerializer(dbContextEngine));
                    mm.SetMemberSerializer(m => m.Watchers,
                        new EnumerableSerializer<AccountBase>(
                            AccountMap.SummaryInfoSerializer(dbContextEngine)));
                });
        }
    }
}
