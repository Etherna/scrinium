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

using Etherna.Scrinium.AspNetCoreSample.Models;
using Etherna.Scrinium.AspNetCoreSample.Models.ModelMaps;
using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.Repositories;
using Etherna.Scrinium.Core.Serialization;
using System.Collections.Generic;

namespace Etherna.Scrinium.AspNetCoreSample.Persistence
{
    /// <summary>
    /// A read-only view over the same database of <see cref="SampleDbContext"/>, registered
    /// with <c>IsReadOnly</c> on its options: any write on it throws, seeding and migrations
    /// are skipped, and the admin dashboard renders it without migration controls.
    /// </summary>
    public class ReadOnlySampleDbContext : DbContext, IReadOnlySampleDbContext
    {
        public IRepository<Cat, string> Cats { get; } = new Repository<Cat, string>("cats");
        public IRepository<Person, string> Persons { get; } = new Repository<Person, string>("persons");

        protected override IEnumerable<IModelMapsCollector> ModelMapsCollectors =>
            [new ModelBaseMap(), new CatMap(), new PersonMap()];
    }
}
