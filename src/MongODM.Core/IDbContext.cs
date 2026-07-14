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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core
{
    /// <summary>
    /// Interface of <see cref="DbContext"/> implementation. Extends the engine members
    /// with the state of the current unit of work.
    /// </summary>
    public interface IDbContext : IDbContextEngine
    {
        // Properties.
        /// <summary>
        /// List of models loaded in the current execution scope with pending changes to save.
        /// </summary>
        IReadOnlyCollection<IEntityModel> ChangedModelsList { get; }

        // Methods.
        /// <summary>
        /// Save current model changes on db.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        Task SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
