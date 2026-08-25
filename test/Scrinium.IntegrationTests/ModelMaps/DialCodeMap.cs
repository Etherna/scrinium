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
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Serialization;
using Etherna.Scrinium.IntegrationTests.Models;
using Etherna.Scrinium.IntegrationTests.Serializers;

namespace Etherna.Scrinium.IntegrationTests.ModelMaps
{
    internal sealed class DialCodeMap : IModelMapsCollector
    {
        public void Register(IDbContextEngine dbContextEngine)
        {
            dbContextEngine.MapRegistry.AddCustomSerializerMap<DialCode>(new DialCodeSerializer());

            dbContextEngine.MapRegistry.AddModelMap<EntityModelBase<DialCode>>(
                "6773a6b6-3a16-4aa5-87e1-118923b86301");
        }
    }
}
