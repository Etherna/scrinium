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

using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.Repositories;
using Etherna.Scrinium.Core.Serialization;
using Etherna.Scrinium.IntegrationTests.ModelMaps;
using Etherna.Scrinium.IntegrationTests.Models;
using System.Collections.Generic;

namespace Etherna.Scrinium.IntegrationTests
{
    public interface IMixedAccessDbContext : IDbContext
    {
        IRepository<Mixtape, string> ArchivedMixtapes { get; }
        IRepository<Mixtape, string> Mixtapes { get; }
        IRepository<Note, string> Notes { get; }
        IRepository<TagBag, string> TagBags { get; }
        IRepository<Track, string> Tracks { get; }
    }

    /// <summary>
    /// A writable db context mixing access levels on the shared database owned by
    /// <see cref="SecondDbContext"/>: it consumes the shared notes collection and an
    /// archived mixtapes collection read-only, and owns its own writable collections,
    /// with the mixtapes referencing the tracks.
    /// </summary>
    internal sealed class MixedAccessDbContext : DbContext, IMixedAccessDbContext
    {
        // Properties.
        //repositories
        public IRepository<Mixtape, string> ArchivedMixtapes { get; } = new Repository<Mixtape, string>(
            new RepositoryOptions<Mixtape>("archivedMixtapes") { IsReadOnly = true });
        public IRepository<Mixtape, string> Mixtapes { get; } = new Repository<Mixtape, string>("mixedMixtapes");
        public IRepository<Note, string> Notes { get; } = new Repository<Note, string>(
            new RepositoryOptions<Note>("notes") { IsReadOnly = true });
        public IRepository<TagBag, string> TagBags { get; } = new Repository<TagBag, string>("mixedTagBags");
        public IRepository<Track, string> Tracks { get; } = new Repository<Track, string>("mixedTracks");

        // Protected properties.
        protected override IEnumerable<IModelMapsCollector> ModelMapsCollectors =>
            [new MixtapeMap(), new NoteMap(), new TagBagMap(), new TrackMap()];
    }
}
