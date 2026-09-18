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
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Etherna.Scrinium.IntegrationTests
{
    public interface ISeedChildDbContext : IDbContext
    {
        IRepository<Note, string> Notes { get; }
    }

    /// <summary>
    /// Writable child of <see cref="ISeedParentDbContext"/>, left unseeded by the fixture and
    /// seeded by the startup seeding tests. Its seed creates a note, and records the instance
    /// it ran on.
    /// </summary>
    internal sealed class SeedChildDbContext(
        ConcurrentDictionary<string, object?> seedingObservations)
        : DbContext, ISeedChildDbContext
    {
        // Consts.
        public const string SeedingInstanceKey = "childSeedingInstance";

        // Fields.
        /* Keeps the exclusive window of this seeding open long enough for a seeding running
         * beside it to meet it, instead of passing by a lucky interleaving. */
        private static readonly TimeSpan seedDuration = TimeSpan.FromMilliseconds(250);

        // Properties.
        //repositories
        public IRepository<Note, string> Notes { get; } = new Repository<Note, string>("notes");

        // Protected properties.
        protected override IEnumerable<IModelMapsCollector> ModelMapsCollectors =>
            [new NoteMap()];

        // Protected methods.
        protected override async Task SeedAsync()
        {
            seedingObservations[SeedingInstanceKey] = this;

            await Task.Delay(seedDuration);
            await Notes.CreateAsync(new Note("seeded by the child"));
        }
    }
}
