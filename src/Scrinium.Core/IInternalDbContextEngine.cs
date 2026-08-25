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

using Etherna.Scrinium.Core.Utility;

namespace Etherna.Scrinium.Core
{
    /// <summary>
    /// The engine surface invoked only inside the library, by the guarded collections it
    /// hands out. Implemented explicitly by <see cref="DbContextEngine"/>.
    /// </summary>
    internal interface IInternalDbContextEngine
    {
        // Properties.
        /// <summary>
        /// The operations in flight on the guarded collections of the engine, entered by every
        /// guarded operation for the span of its forwarded call and drained by the exclusive
        /// access window before running its work.
        /// </summary>
        InFlightOperationsCounter InFlightOperations { get; }
    }
}
