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
using Etherna.MongODM.Core;
using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.Serialization;
using Etherna.MongODM.IntegrationTests.Models;

namespace Etherna.MongODM.IntegrationTests.ModelMaps
{
    internal sealed class VoucherMap : IModelMapsCollector
    {
        public void Register(IDbContextEngine dbContextEngine)
        {
            dbContextEngine.MapRegistry.AddModelMap<EntityModelBase<ObjectId>>(
                "b81c2d94-56af-4e7c-8d13-f0a92c47d6e5");

            dbContextEngine.MapRegistry.AddModelMap<Voucher>(
                "c25e8f07-31db-49a8-b6c4-5d18e9f3a02b");
        }
    }
}
