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

using Etherna.Scrinium.Core.Domain.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.Scrinium.Core.Repositories
{
    /// <summary>
    /// Batch loading surface of the library repositories, consumed by the members preload:
    /// load the full documents of the given models by their ids, with one query per
    /// bounded ids chunk. The results merge in place into the scope loaded instances
    /// through the identity map: summary models upgrade to full. Custom repository
    /// implementations without this interface preload with per instance loads.
    /// </summary>
    internal interface IFullModelsLoader
    {
        Task LoadFullModelsAsync(
            IEnumerable<IEntityModel> models,
            CancellationToken cancellationToken = default);
    }
}
