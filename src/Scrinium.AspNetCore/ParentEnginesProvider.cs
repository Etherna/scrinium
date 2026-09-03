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

using Etherna.Scrinium.Core;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Etherna.Scrinium.AspNetCore
{
    internal sealed class ParentEnginesProvider(
        IEnumerable<DbContextRegistration> dbContextRegistrations,
        IServiceProvider serviceProvider) : IParentEnginesProvider
    {
        // Methods.
        public IEnumerable<IDbContextEngine> GetParentEngines(Type dbContextType)
        {
            ArgumentNullException.ThrowIfNull(dbContextType);

            /* A parent declares its children by their registered db context types: each
             * declared type resolves to its registration like the scope attach resolves
             * its instance, so the parent relation mirrors the dependency injection one.
             * Engines are keyed singletons, resolved on demand by implementation type. */
            return dbContextRegistrations
                .Where(registration => registration.ImplementationType != dbContextType)
                .Where(registration => registration.Options.ChildDbContextTypes
                    .Any(childDbContextType => dbContextRegistrations
                        .LastOrDefault(childRegistration =>
                            childRegistration.ServiceType == childDbContextType ||
                            childRegistration.ImplementationType == childDbContextType)?.ImplementationType == dbContextType))
                .Select(registration => serviceProvider.GetRequiredKeyedService<IDbContextEngine>(registration.ImplementationType));
        }
    }
}
