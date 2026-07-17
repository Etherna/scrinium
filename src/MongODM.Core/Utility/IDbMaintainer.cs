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

using Etherna.MongODM.Core.ProxyModels;
using Etherna.MongODM.Core.Repositories;

namespace Etherna.MongODM.Core.Utility
{
    /// <summary>
    /// Interface for <see cref="DbMaintainer"/> implementation.
    /// </summary>
    public interface IDbMaintainer : IDbContextEngineInitializable
    {
        // Methods.
        /// <summary>
        /// Method to invoke when an auditable model is updated.
        /// </summary>
        /// <typeparam name="TKey">Updated model Key type</typeparam>
        /// <param name="updatedModel">The updated model</param>
        /// <param name="referenceRepository">The repository of the updated model</param>
        void OnUpdatedModel<TKey>(IAuditable updatedModel, IRepository referenceRepository);
    }
}