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

using Etherna.MongoDB.Driver;
using Etherna.MongoDB.Driver.Linq;
using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.Serialization;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Etherna.Scrinium.IntegrationTests
{
    public interface ISeedParentDbContext : IDbContext
    { }

    /// <summary>
    /// Writable parent of <see cref="ISeedChildDbContext"/>, left unseeded by the fixture and
    /// seeded by the startup seeding tests. Its seed reads the notes seeded by its child, and
    /// records them with the child instance attached to its scope.
    /// </summary>
    internal sealed class SeedParentDbContext(
        ConcurrentDictionary<string, object?> seedingObservations)
        : DbContext, ISeedParentDbContext
    {
        // Consts.
        public const string AttachedChildInstanceKey = "parentAttachedChildInstance";
        public const string ChildNotesKey = "parentReadChildNotes";

        // Protected properties.
        protected override IEnumerable<IModelMapsCollector> ModelMapsCollectors => [];

        // Protected methods.
        protected override async Task SeedAsync()
        {
            var child = ChildDbContexts.OfType<ISeedChildDbContext>().Single();
            seedingObservations[AttachedChildInstanceKey] = child;
            seedingObservations[ChildNotesKey] = await child.Notes.QueryElementsAsync(
                notes => notes.Select(note => note.Text).ToListAsync());
        }
    }
}
