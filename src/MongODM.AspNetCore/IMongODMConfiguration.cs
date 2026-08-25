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

using Etherna.MongODM.Core;
using Etherna.MongODM.Core.Options;
using System;

namespace Etherna.MongODM.AspNetCore
{
    public interface IMongODMConfiguration
    {
        bool IsFrozen { get; }

        // Methods.
        IMongODMConfiguration AddDbContext<TDbContext>(
            Action<DbContextOptions>? dbContextOptionsConfig = null)
            where TDbContext : DbContext, new();

        IMongODMConfiguration AddDbContext<TDbContext>(
            Func<IServiceProvider, TDbContext> dbContextCreator,
            Action<DbContextOptions>? dbContextOptionsConfig = null)
            where TDbContext : DbContext;

        IMongODMConfiguration AddDbContext<TDbContext, TDbContextImpl>(
            Action<DbContextOptions>? dbContextOptionsConfig = null)
            where TDbContext : class, IDbContext
            where TDbContextImpl : DbContext, TDbContext, new();

        IMongODMConfiguration AddDbContext<TDbContext, TDbContextImpl>(
            Func<IServiceProvider, TDbContextImpl> dbContextCreator,
            Action<DbContextOptions>? dbContextOptionsConfig = null)
            where TDbContext : class, IDbContext
            where TDbContextImpl : DbContext, TDbContext;

        /// <summary>
        /// Freeze configuration.
        /// </summary>
        void Freeze(IMongODMOptionsBuilder mongODMOptionsBuilder);
    }
}
