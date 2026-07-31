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
using Etherna.MongODM.Core.Migration;
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.Core.Serialization;
using Etherna.MongODM.IntegrationTests.ModelMaps;
using Etherna.MongODM.IntegrationTests.Models;
using System.Collections.Generic;

namespace Etherna.MongODM.IntegrationTests
{
    public interface IMigrationsDbContext : IDbContext
    {
        IRepository<Digest, string> Digests { get; }
        IEnumerable<DocumentMigration> DocumentMigrations { get; set; }
        IRepository<Note, string> Notes { get; }
    }

    internal sealed class MigrationsDbContext : DbContext, IMigrationsDbContext
    {
        // Properties.
        //repositories
        public IRepository<Digest, string> Digests { get; } = new Repository<Digest, string>("digests");
        public IRepository<Note, string> Notes { get; } = new Repository<Note, string>("notes");

        /* Test hook: each test assigns the document migrations to run. */
        public IEnumerable<DocumentMigration> DocumentMigrations { get; set; } = [];
        public override IEnumerable<DocumentMigration> DocumentMigrationList => DocumentMigrations;

        // Protected properties.
        protected override IEnumerable<IModelMapsCollector> ModelMapsCollectors =>
            [new DigestMap(), new NoteMap()];
    }
}
