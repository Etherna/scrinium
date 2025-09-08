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

using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.Domain.Models.DbMigrationOpAgg;
using Etherna.MongODM.Core.Serialization;

namespace Etherna.MongODM.Core.Domain.ModelMaps
{
    internal sealed class DbMigrationOperationMap : IModelMapsCollector
    {
        public void Register(IDbContext dbContext)
        {
            dbContext.MapRegistry.AddModelMap<BuildNewIndexesMigrationLog>("555eed70-62e2-4d85-ac47-75bae10eefa9");
            dbContext.MapRegistry.AddModelMap<DbMigrationOperation>("afdb63c9-791b-41f8-8216-556e233df0de");
            dbContext.MapRegistry.AddModelMap<DeleteOldIndexesMigrationLog>("ac9d8011-6247-4365-b8ca-ac8401f838a1");
            dbContext.MapRegistry.AddModelMap<DocumentMigrationLog>("d2b49514-464e-4b28-8b38-ad2d0cc69d3e");
            dbContext.MapRegistry.AddModelMap<MigrationLogBase>("1696c0c9-d615-44d9-ab9b-4e3618164185");
            
            //obsolete
#pragma warning disable CS0618 // Type or member is obsolete
            dbContext.MapRegistry.AddModelMap<IndexMigrationLog>("24d65670-a3c3-443c-977a-51112df04e2a");
#pragma warning restore CS0618 // Type or member is obsolete
        }
    }
}
