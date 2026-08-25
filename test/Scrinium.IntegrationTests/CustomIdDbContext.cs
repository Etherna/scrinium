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

using Etherna.MongoDB.Bson;
using Etherna.MongoDB.Driver;
using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.Repositories;
using Etherna.Scrinium.Core.Serialization;
using Etherna.Scrinium.IntegrationTests.ModelMaps;
using Etherna.Scrinium.IntegrationTests.Models;
using System;
using System.Collections.Generic;

namespace Etherna.Scrinium.IntegrationTests
{
    public interface ICustomIdDbContext : IDbContext
    {
        IRepository<Artifact, Fingerprint> Artifacts { get; }
        IRepository<Badge, int> Badges { get; }
        IRepository<Locker, DialCode> Lockers { get; }
        IRepository<Seal, string> Seals { get; }
        IRepository<Ticket, Guid> Tickets { get; }
        IRepository<Voucher, ObjectId> Vouchers { get; }
    }

    /// <summary>
    /// A db context whose entities use ids of assorted non default types: a custom
    /// serialized value type on <see cref="Artifact"/> (MODM-176), int on
    /// <see cref="Badge"/>, a custom value type serialized as document on
    /// <see cref="Locker"/> (MODM-222), Guid on <see cref="Ticket"/>, native ObjectId on
    /// <see cref="Voucher"/>, with the custom type also serialized as plain member by
    /// <see cref="Seal"/>.
    /// </summary>
    internal sealed class CustomIdDbContext : DbContext, ICustomIdDbContext
    {
        // Properties.
        //repositories
        public IRepository<Artifact, Fingerprint> Artifacts { get; } = new Repository<Artifact, Fingerprint>(
            new RepositoryOptions<Artifact>("artifacts")
            {
                IndexBuilders =
                [
                    (Builders<Artifact>.IndexKeys.Ascending(a => a.Label).Descending(a => a.Id),
                        new CreateIndexOptions<Artifact> { Name = "label_by_fingerprint" }),
                    (Builders<Artifact>.IndexKeys.Text(a => a.Label),
                        new CreateIndexOptions<Artifact> { Name = "label_text" }),
                    (Builders<Artifact>.IndexKeys.Wildcard(),
                        new CreateIndexOptions<Artifact> { Name = "wildcard_all" })
                ]
            });
        public IRepository<Badge, int> Badges { get; } = new Repository<Badge, int>("badges");
        public IRepository<Locker, DialCode> Lockers { get; } = new Repository<Locker, DialCode>("lockers");
        public IRepository<Seal, string> Seals { get; } = new Repository<Seal, string>(
            new RepositoryOptions<Seal>("seals")
            {
                IndexBuilders =
                [
                    //without explicit name, pinning the name rendered from the keys
                    (Builders<Seal>.IndexKeys.Ascending(s => s.ArtifactFingerprint),
                        new CreateIndexOptions<Seal> { Unique = true }),
                    (Builders<Seal>.IndexKeys.Hashed(s => s.ArtifactFingerprint),
                        new CreateIndexOptions<Seal> { Name = "fingerprint_hashed" })
                ]
            });
        public IRepository<Ticket, Guid> Tickets { get; } = new Repository<Ticket, Guid>("tickets");
        public IRepository<Voucher, ObjectId> Vouchers { get; } = new Repository<Voucher, ObjectId>("vouchers");

        // Protected properties.
        protected override IEnumerable<IModelMapsCollector> ModelMapsCollectors =>
            [new FingerprintMap(), new DialCodeMap(), new ArtifactMap(), new BadgeMap(), new LockerMap(), new SealMap(), new TicketMap(), new VoucherMap()];
    }
}
