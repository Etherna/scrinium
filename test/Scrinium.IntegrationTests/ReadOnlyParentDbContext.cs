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

using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.Repositories;
using Etherna.Scrinium.Core.Serialization;
using Etherna.Scrinium.IntegrationTests.ModelMaps;
using Etherna.Scrinium.IntegrationTests.Models;
using System.Collections.Generic;

namespace Etherna.Scrinium.IntegrationTests
{
    public interface IReadOnlyParentDbContext : IDbContext
    {
        IRepository<Journal, string> Journals { get; }
    }

    /// <summary>
    /// A read-only parent of <see cref="ISecondDbContext"/>, consuming the database owned by
    /// <see cref="ParentDbContext"/>: its documents belong to another application, so the
    /// dependencies propagation of the child models never enqueues toward it.
    /// </summary>
    internal sealed class ReadOnlyParentDbContext : DbContext, IReadOnlyParentDbContext
    {
        // Properties.
        //repositories
        public IRepository<Journal, string> Journals { get; } = new Repository<Journal, string>("journals");

        // Protected properties.
        protected override IEnumerable<IModelMapsCollector> ModelMapsCollectors =>
            [new JournalMap()];
    }
}
