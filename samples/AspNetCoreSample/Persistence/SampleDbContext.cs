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

using Etherna.MongODM.AspNetCoreSample.Models;
using Etherna.MongODM.AspNetCoreSample.Models.ModelMaps;
using Etherna.MongODM.Core;
using Etherna.MongODM.Core.Migration;
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.Core.Serialization;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Etherna.MongODM.AspNetCoreSample.Persistence
{
    public class SampleDbContext : DbContext, ISampleDbContext
    {
        public IRepository<Cat, string> Cats { get; } = new Repository<Cat, string>("cats");

        // Rewrite each cat document with the active schema. Try it from the admin dashboard,
        // also as dry run: failing documents report into the operation logs without persisting.
        public override IEnumerable<DocumentMigration> DocumentMigrationList =>
            [new DocumentMigration<Cat, string>(Cats)];

        protected override IEnumerable<IModelMapsCollector> ModelMapsCollectors =>
            [new ModelBaseMap(), new CatMap()];

        protected override Task SeedAsync()
        {
            // Seed here.

            return base.SeedAsync();
        }
    }
}
