// Copyright 2020-present Etherna SA
// This file is part of Scrinium.
//
// Scrinium is free software: you can redistribute it and/or modify it under the terms of the
// GNU Lesser General Public License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
//
// Scrinium is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY;
// without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License along with Scrinium.
// If not, see <https://www.gnu.org/licenses/>.

using Etherna.MongoDB.Bson;
using Etherna.MongoDB.Bson.Serialization.Serializers;
using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Serialization;
using Etherna.Scrinium.IntegrationTests.Models;
using System;

namespace Etherna.Scrinium.IntegrationTests.ModelMaps
{
    internal sealed class TicketMap : IModelMapsCollector
    {
        public void Register(IDbContextEngine dbContextEngine)
        {
            /* The driver default Guid serializer has an unspecified representation,
             * failing at use: the custom serializer map serves every Guid of the db
             * context, the entity id included. */
            dbContextEngine.MapRegistry.AddCustomSerializerMap<Guid>(
                new GuidSerializer(GuidRepresentation.Standard));

            dbContextEngine.MapRegistry.AddModelMap<EntityModelBase<Guid>>(
                "e5f2a8c1-4b7d-4890-b3a2-91c6d0e47f38");

            dbContextEngine.MapRegistry.AddModelMap<Ticket>(
                "a1d9c3e7-2f58-4b06-8c41-7d3e9f0b52c6");
        }
    }
}
