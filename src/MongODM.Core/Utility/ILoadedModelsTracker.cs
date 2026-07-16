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

namespace Etherna.MongODM.Core.Utility
{
    /// <summary>
    /// Interface for <see cref="LoadedModelsTracker"/> implementation.
    /// </summary>
    public interface ILoadedModelsTracker : IDbContextEngineInitializable
    {
        // Properties.
        /// <summary>
        /// List of models loaded in the current execution scope.
        /// </summary>
        IReadOnlyCollection<IEntityModel> LoadedModels { get; }

        // Methods.
        /// <summary>
        /// Clear all tracked models from the current execution scope.
        /// </summary>
        void ClearTracked();

        /// <summary>
        /// Start to track a loaded model.
        /// </summary>
        /// <param name="model">The model</param>
        void TrackModel(IEntityModel model);

        /// <summary>
        /// Stop to track a model.
        /// </summary>
        /// <param name="model">The model</param>
        void UntrackModel(IEntityModel model);
    }
}
