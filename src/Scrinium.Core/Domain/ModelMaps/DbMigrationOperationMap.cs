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
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Domain.Models.DbMigrationOpAgg;
using Etherna.Scrinium.Core.Serialization;
using System;

namespace Etherna.Scrinium.Core.Domain.ModelMaps
{
    internal sealed class DbMigrationOperationMap : IModelMapsCollector
    {
        public void Register(IDbContextEngine dbContextEngine)
        {
            dbContextEngine.MapRegistry.AddModelMap<BuildNewIndexesMigrationLog>("555eed70-62e2-4d85-ac47-75bae10eefa9");
            dbContextEngine.MapRegistry.AddModelMap<DbMigrationOperation>("afdb63c9-791b-41f8-8216-556e233df0de",
                mm =>
                {
                    mm.AutoMap();

                    // Set dates representation.
                    mm.GetMemberMap(m => m.CompletedDateTime).SetSerializer(
                        new NullableSerializer<DateTimeOffset>(new DateTimeOffsetSerializer(BsonType.DateTime)));
                });
            dbContextEngine.MapRegistry.AddModelMap<DeleteOldIndexesMigrationLog>("ac9d8011-6247-4365-b8ca-ac8401f838a1");
            dbContextEngine.MapRegistry.AddModelMap<DocumentMigrationError>("15b8f6c8-8e94-4849-a3ce-4f0eb2cbb556");
            dbContextEngine.MapRegistry.AddModelMap<DocumentMigrationLog>("d2b49514-464e-4b28-8b38-ad2d0cc69d3e");
            dbContextEngine.MapRegistry.AddModelMap<MigrationLogBase>("1696c0c9-d615-44d9-ab9b-4e3618164185",
                mm =>
                {
                    mm.AutoMap();

                    // Set dates representation.
                    mm.GetMemberMap(m => m.CreationDateTime).SetSerializer(
                        new DateTimeOffsetSerializer(BsonType.DateTime));
                });
            
            //obsolete
#pragma warning disable CS0618 // Type or member is obsolete
            dbContextEngine.MapRegistry.AddModelMap<IndexMigrationLog>("24d65670-a3c3-443c-977a-51112df04e2a");
#pragma warning restore CS0618 // Type or member is obsolete
        }
    }
}
