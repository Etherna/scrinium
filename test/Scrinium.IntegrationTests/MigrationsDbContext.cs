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
using Etherna.Scrinium.Core.Migration;
using Etherna.Scrinium.Core.Repositories;
using Etherna.Scrinium.Core.Serialization;
using Etherna.Scrinium.IntegrationTests.ModelMaps;
using Etherna.Scrinium.IntegrationTests.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace Etherna.Scrinium.IntegrationTests
{
    public interface IMigrationsDbContext : IDbContext
    {
        IRepository<Digest, string> Digests { get; }
        IEnumerable<DocumentMigration> DocumentMigrations { get; set; }
        IRepository<Note, string> Notes { get; }
    }

    internal sealed class MigrationsDbContext(ILogger logger)
        : DbContext(logger), IMigrationsDbContext
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
