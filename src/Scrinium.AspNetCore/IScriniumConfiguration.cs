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

using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.Options;
using System;

namespace Etherna.Scrinium.AspNetCore
{
    public interface IScriniumConfiguration
    {
        bool IsFrozen { get; }

        // Methods.
        IScriniumConfiguration AddDbContext<TDbContext>(
            Action<DbContextOptions>? dbContextOptionsConfig = null)
            where TDbContext : DbContext, new();

        IScriniumConfiguration AddDbContext<TDbContext>(
            Func<IServiceProvider, TDbContext> dbContextCreator,
            Action<DbContextOptions>? dbContextOptionsConfig = null)
            where TDbContext : DbContext;

        IScriniumConfiguration AddDbContext<TDbContext, TDbContextImpl>(
            Action<DbContextOptions>? dbContextOptionsConfig = null)
            where TDbContext : class, IDbContext
            where TDbContextImpl : DbContext, TDbContext, new();

        IScriniumConfiguration AddDbContext<TDbContext, TDbContextImpl>(
            Func<IServiceProvider, TDbContextImpl> dbContextCreator,
            Action<DbContextOptions>? dbContextOptionsConfig = null)
            where TDbContext : class, IDbContext
            where TDbContextImpl : DbContext, TDbContext;

        /// <summary>
        /// Freeze configuration.
        /// </summary>
        void Freeze(IScriniumOptionsBuilder mongODMOptionsBuilder);
    }
}
