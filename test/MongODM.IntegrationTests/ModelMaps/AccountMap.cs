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
using Etherna.MongODM.Core.Serialization;
using Etherna.MongODM.IntegrationTests.Models;

namespace Etherna.MongODM.IntegrationTests.ModelMaps
{
    internal sealed class AccountMap : IModelMapsCollector
    {
        public void Register(IDbContextEngine dbContextEngine)
        {
            dbContextEngine.MapRegistry.AddModelMap<AccountBase>("6f3a2b10-83a0-4b00-9c00-000000000001");
            dbContextEngine.MapRegistry.AddModelMap<Web2Account>("6f3a2b10-83a0-4b00-9c00-000000000002");
            dbContextEngine.MapRegistry.AddModelMap<Web3Account>("6f3a2b10-83a0-4b00-9c00-000000000003");
        }
    }
}
