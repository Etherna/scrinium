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

using Etherna.Scrinium.Core.Repositories;
using System;

namespace Etherna.Scrinium.Core.ProxyModels
{
    /// <summary>
    /// Implemented by the source generated proxy model types. Infrastructure interface,
    /// not intended for application use: the framework identifies proxy instances with it,
    /// and binds them to their operation scope right after creation.
    /// </summary>
    public interface IProxyModel
    {
        // Properties.
        /// <summary>
        /// The current type of the model document, when it doesn't match the instance type
        /// anymore: the document changed type after the instance materialized, and any
        /// application interaction with the instance throws
        /// <see cref="Exceptions.ScriniumOutdatedModelTypeException"/>. Null while the
        /// instance type is valid.
        /// </summary>
        Type? OutdatedModelType { get; }

        // Methods.
        /// <summary>
        /// Bind the proxy to the scope of the operation creating it: the db context tracking
        /// its changes, and the source repository hosting its document. A null db context
        /// disables change candidate marking, keeping the instance out of the unit of work.
        /// </summary>
        /// <param name="dbContext">The change tracking db context scope</param>
        /// <param name="sourceRepository">The source repository hosting the model document</param>
        void BindProxy(IDbContext? dbContext, IRepository sourceRepository);

        /// <summary>
        /// Invalidate the instance because its document now has another type of its
        /// hierarchy: the instance type can't upgrade, so any application interaction
        /// with the instance starts throwing
        /// <see cref="Exceptions.ScriniumOutdatedModelTypeException"/>.
        /// </summary>
        /// <param name="actualModelType">The current model type of the document</param>
        void SetOutdatedModelType(Type actualModelType);
    }
}
