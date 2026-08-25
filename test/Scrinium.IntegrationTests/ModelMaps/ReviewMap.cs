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
    internal sealed class ReviewMap : IModelMapsCollector
    {
        public void Register(IDbContextEngine dbContextEngine)
        {
            dbContextEngine.MapRegistry.AddModelMap<Review>(
                "b0a54751-9c25-4b72-8a66-b4c4625cb47b",
                mm =>
                {
                    mm.AutoMap();

                    // Set members with custom serializers.
                    mm.SetMemberSerializer(r => r.Item!, ItemMap.MinimalReferenceSerializer(dbContextEngine));
                });
        }

        /// <summary>
        /// Summary information serializer, including the review text
        /// </summary>
        public static ReferenceSerializer<Review, string> SummaryInfoSerializer(IDbContextEngine dbContextEngine) =>
            ReferenceSerializer.Create(dbContextEngine, config =>
            {
                config.AddModelMap<ModelBase>("4a1163a5-457b-498c-bf12-b2d5899bcbdd");
                config.AddModelMap<EntityModelBase<string>>("d963091a-34ed-4417-8f6c-558d021a34a0", mm =>
                {
                    mm.MapIdMember(m => m.Id);
                    mm.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId));
                });
                config.AddModelMap<Review>("00047f6e-3eca-4851-9aae-64f0f11d227e", mm =>
                {
                    mm.MapMember(r => r.Text);
                });
            },
            sourceRepository: (ITestDbContext dbContext) => dbContext.Reviews);
    }
}
