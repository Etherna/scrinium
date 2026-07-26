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
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.Core.Serialization;
using System.Collections.Generic;

namespace Etherna.MongODM.AspNetCoreSample.Persistence
{
    /// <summary>
    /// A read-only view over the same database of <see cref="SampleDbContext"/>, registered
    /// with <c>IsReadOnly</c> on its options: any write on it throws, seeding and migrations
    /// are skipped, and the admin dashboard renders it without migration controls.
    /// </summary>
    public class ReadOnlySampleDbContext : DbContext, IReadOnlySampleDbContext
    {
        public IRepository<Cat, string> Cats { get; } = new Repository<Cat, string>("cats");

        protected override IEnumerable<IModelMapsCollector> ModelMapsCollectors =>
            [new ModelBaseMap(), new CatMap()];
    }
}
