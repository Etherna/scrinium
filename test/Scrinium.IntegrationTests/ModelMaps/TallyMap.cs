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
    internal sealed class TallyMap : IModelMapsCollector
    {
        public void Register(IDbContextEngine dbContextEngine)
        {
            dbContextEngine.MapRegistry.AddModelMap<Score>("7b1e6c2a-9d3f-4e58-b0a1-5c6d7e8f9a0b");
            dbContextEngine.MapRegistry.AddModelMap<Tally>("e3f4a5b6-c7d8-4e9f-a0b1-c2d3e4f5a6b7");
        }
    }
}
