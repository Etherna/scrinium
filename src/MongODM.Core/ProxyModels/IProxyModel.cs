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

using Etherna.MongODM.Core.Repositories;

namespace Etherna.MongODM.Core.ProxyModels
{
    /// <summary>
    /// Implemented by the source generated proxy model types. Infrastructure interface,
    /// not intended for application use: the framework identifies proxy instances with it,
    /// and binds them to their operation scope right after creation.
    /// </summary>
    public interface IProxyModel
    {
        /// <summary>
        /// Bind the proxy to the scope of the operation creating it: the db context tracking
        /// its changes, and the source repository hosting its document. Null bindings disable
        /// change candidate marking and lazy loading respectively.
        /// </summary>
        /// <param name="dbContext">The change tracking db context scope</param>
        /// <param name="sourceRepository">The source repository hosting the model document</param>
        void BindProxy(IDbContext? dbContext, IRepository? sourceRepository);
    }
}
