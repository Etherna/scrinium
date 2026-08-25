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
    /// <summary>
    /// A db context mapping a model with an interface typed member: the driver
    /// discriminated interface serializer requires the registered serializer for object
    /// to be the driver ObjectSerializer, so the engine build must succeed with it in
    /// place (MODM-231).
    /// </summary>
    internal sealed class InterfaceMemberDbContext : DbContext
    {
        // Properties.
        //repositories
        public IRepository<Notice, string> Notices { get; } = new Repository<Notice, string>("notices");

        // Protected properties.
        protected override IEnumerable<IModelMapsCollector> ModelMapsCollectors =>
            [new NoticeMap()];

        // Helpers.
        private sealed class NoticeMap : IModelMapsCollector
        {
            public void Register(IDbContextEngine dbContextEngine)
            {
                dbContextEngine.MapRegistry.AddModelMap<Notice>("9c1de24b-2f71-4a20-8e5c-000000000003");
            }
        }
    }
}
