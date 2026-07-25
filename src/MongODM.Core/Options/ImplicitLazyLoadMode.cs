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

namespace Etherna.MongODM.Core.Options
{
    /// <summary>
    /// How the db context reacts to an implicit lazy load: a member of a summary model, not
    /// loaded and not preloaded with <see cref="IDbContext.LoadValuesAsync{TModel}(TModel, System.Linq.Expressions.Expression{System.Func{TModel, object?}}[])"/>,
    /// read through its property or a domain method. The load is synchronous over the db
    /// call, so it degrades the reading path: prefer explicit preloads on performance
    /// sensitive code.
    /// </summary>
    public enum ImplicitLazyLoadMode
    {
        /// <summary>Load silently.</summary>
        Silent,

        /// <summary>Deny the load, throwing <see cref="Exceptions.MongodmLazyLoadingException"/>.</summary>
        Throw,

        /// <summary>Load, logging a warning once per member, per db context scope. The default.</summary>
        Warn
    }
}
