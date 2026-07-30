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

namespace Etherna.MongODM.Core.Exceptions
{
    /// <summary>
    /// Thrown at engine build when a model map member serializes an entity model as a full
    /// embedded document instead of referencing it: lazy loading, saving and identity of an
    /// embedded entity would be undefined. At most, a reference can denormalize every member:
    /// it is still a reference, with its own source.
    /// </summary>
    public class MongodmEmbeddedEntityModelException : Exception
    {
        // Constructors.
        public MongodmEmbeddedEntityModelException()
        { }

        public MongodmEmbeddedEntityModelException(string message)
            : base(message)
        { }

        public MongodmEmbeddedEntityModelException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
}
