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
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.Core.Serialization;
using Etherna.MongODM.IntegrationTests.ModelMaps;
using Etherna.MongODM.IntegrationTests.Models;
using System.Collections.Generic;

namespace Etherna.MongODM.IntegrationTests
{
    /// <summary>
    /// A db context parent of <see cref="ISecondDbContext"/>: exercises the children save
    /// cascading and the cross db context references, like production contexts sharing models.
    /// </summary>
    public interface IParentDbContext : IDbContext
    {
        IRepository<Journal, string> Journals { get; }
    }

    internal sealed class ParentDbContext : DbContext, IParentDbContext
    {
        // Properties.
        //repositories
        public IRepository<Journal, string> Journals { get; } = new Repository<Journal, string>("journals");

        // Protected properties.
        protected override IEnumerable<IModelMapsCollector> ModelMapsCollectors =>
            [new JournalMap()];
    }
}
