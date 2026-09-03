// Copyright 2020-present Etherna SA
// This file is part of Scrinium.
// 
// Scrinium is free software: you can redistribute it and/or modify it under the terms of the
// GNU Lesser General Public License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// 
// Scrinium is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY;
// without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public License along with Scrinium.
// If not, see <https://www.gnu.org/licenses/>.

using Etherna.MongoDB.Driver;
using Etherna.Scrinium.Core.Options;
using Etherna.Scrinium.Core.Repositories;
using System.Collections.Generic;

namespace Etherna.Scrinium.Core
{
    public interface IDbContextBuilder
    {
        /// <summary>
        /// Attach this db context instance to its engine, initializing the unit of work state.
        /// The engine lifetime belongs to whoever built it, not to the attached db context.
        /// </summary>
        void AttachToEngine(
            IDbContextEngine engine,
            IEnumerable<IDbContext> childDbContexts,
            IRepositoryRegistry repositoryRegistry);

        /// <summary>
        /// Build and initialize a new engine from this db context definitions, without attaching to it.
        /// </summary>
        /// <returns>The new initialized engine, owned by the caller</returns>
        IDbContextEngine BuildEngine(
            IDbDependencies dependencies,
            IMongoClient mongoClient,
            IDbContextOptions options);
    }
}
