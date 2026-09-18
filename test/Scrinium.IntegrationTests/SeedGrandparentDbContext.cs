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
using Etherna.Scrinium.Core.Serialization;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Etherna.Scrinium.IntegrationTests
{
    public interface ISeedGrandparentDbContext : IDbContext
    { }

    /// <summary>
    /// Writable parent of <see cref="ISeedParentDbContext"/>, itself parent of
    /// <see cref="ISeedChildDbContext"/>: a hierarchy deeper than a parent and its child, left
    /// unseeded by the fixture. Its seed records the seeding state it finds on its child.
    /// </summary>
    internal sealed class SeedGrandparentDbContext(
        ConcurrentDictionary<string, object?> seedingObservations)
        : DbContext, ISeedGrandparentDbContext
    {
        // Consts.
        public const string IsParentSeededKey = "grandparentFoundParentSeeded";

        // Protected properties.
        protected override IEnumerable<IModelMapsCollector> ModelMapsCollectors => [];

        // Protected methods.
        protected override Task SeedAsync()
        {
            seedingObservations[IsParentSeededKey] =
                ChildDbContexts.OfType<ISeedParentDbContext>().Single().IsSeeded;
            return Task.CompletedTask;
        }
    }
}
