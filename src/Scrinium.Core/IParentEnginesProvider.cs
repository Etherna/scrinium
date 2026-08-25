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

using System;
using System.Collections.Generic;

namespace Etherna.Scrinium.Core
{
    /// <summary>
    /// Resolves the engines of the parent db contexts of a db context type: the db contexts
    /// registered by the application declaring it as their child with
    /// <see cref="Options.DbContextOptions.ParentFor{TDbContext}"/>. The dependencies
    /// propagation crosses the engines of the application through this component, reaching
    /// the documents of the parent db contexts referencing a changed or deleted child model.
    /// </summary>
    public interface IParentEnginesProvider
    {
        /// <summary>
        /// The engines of the parent db contexts of the given db context type. Engines
        /// build on demand: a parent engine not used yet by the application builds at its
        /// first resolution.
        /// </summary>
        /// <param name="dbContextType">The child db context type</param>
        /// <returns>The parent db context engines</returns>
        IEnumerable<IDbContextEngine> GetParentEngines(Type dbContextType);
    }
}
