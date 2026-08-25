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

using Etherna.MongODM.Core.Domain.Models;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core.Repositories
{
    /// <summary>
    /// Creation surface of the library repositories, consumed by the new referred models auto
    /// creation: create the model document and start its change tracking, without flushing the
    /// current unit of work like the public create does. Custom repository implementations
    /// without this interface auto create with the public create.
    /// </summary>
    internal interface INewReferredModelsCreator
    {
        Task CreateNewReferredModelAsync(
            IEntityModel model,
            CancellationToken cancellationToken = default);
    }
}
