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

using Etherna.MongODM.Core;
using Etherna.MongODM.Core.Extensions;
using Etherna.MongODM.Core.Serialization;
using Etherna.MongODM.Core.Serialization.Serializers;
using Etherna.MongODM.IntegrationTests.Models;

namespace Etherna.MongODM.IntegrationTests.ModelMaps
{
    internal sealed class BlogMap : IModelMapsCollector
    {
        public void Register(IDbContext dbContext)
        {
            dbContext.MapRegistry.AddModelMap<Blog>(
                "4cd47c3a-0495-4724-a954-c8c64ba8d6e2",
                mm =>
                {
                    mm.AutoMap();

                    // Set members with custom serializers.
                    mm.SetMemberSerializer(b => b.LastPost!, PostMap.PreviewInfoSerializer(dbContext));
                    mm.SetMemberSerializer(b => b.Posts,
                        new EnumerableSerializer<Post>(
                            PostMap.ReferenceSerializer(dbContext)));
                });
        }
    }
}
