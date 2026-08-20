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

using Etherna.MongODM.Core.Options;
using System;

namespace Etherna.MongODM.AspNetCore
{
    /// <summary>
    /// A db context registered with the service collection: the service type exposed to the
    /// application, the implementation type keying its engine, and its options.
    /// </summary>
    /// <param name="ServiceType">The registered db context service type</param>
    /// <param name="ImplementationType">The db context implementation type</param>
    /// <param name="Options">The db context options</param>
    internal sealed record DbContextRegistration(
        Type ServiceType,
        Type ImplementationType,
        IDbContextOptions Options);
}
