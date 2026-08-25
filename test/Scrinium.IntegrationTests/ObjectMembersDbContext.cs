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
using Etherna.MongODM.IntegrationTests.Models;
using System.Collections.Generic;

namespace Etherna.MongODM.IntegrationTests
{
    public interface IObjectMembersDbContext : IDbContext
    {
        IRepository<Capsule, string> Capsules { get; }
    }

    /// <summary>
    /// A db context hosting object shaped members (a plain object payload and a metadata
    /// bag on <see cref="Capsule"/>) beside another mapped model type
    /// (<see cref="Secret"/>), reachable by discriminator: object shaped values must stay
    /// guarded by the allowed types of the driver object serializer (MODM-231).
    /// </summary>
    internal sealed class ObjectMembersDbContext : DbContext, IObjectMembersDbContext
    {
        // Consts.
        public const string CapsuleSchemaId = "9c1de24b-2f71-4a20-8e5c-000000000001";
        public const string SecretSchemaId = "9c1de24b-2f71-4a20-8e5c-000000000002";

        // Properties.
        //repositories
        public IRepository<Capsule, string> Capsules { get; } = new Repository<Capsule, string>("capsules");

        // Protected properties.
        protected override IEnumerable<IModelMapsCollector> ModelMapsCollectors =>
            [new ObjectMembersMap()];

        // Helpers.
        private sealed class ObjectMembersMap : IModelMapsCollector
        {
            public void Register(IDbContextEngine dbContextEngine)
            {
                dbContextEngine.MapRegistry.AddModelMap<Capsule>(CapsuleSchemaId);
                dbContextEngine.MapRegistry.AddModelMap<Secret>(SecretSchemaId);
            }
        }
    }
}
