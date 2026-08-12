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
    /// Thrown at engine build when a model map schema of an entity model type maps an
    /// invalid document id member: a member that is not the implicit implementation of the
    /// entity id contract (the typed id addressed by the framework and the persisted
    /// identity must be the same member), or a member whose serializer represents a
    /// composite (an entity id must serialize to a value, and a document valued id would
    /// render inside the repository id filters as an operator expression).
    /// </summary>
    public class MongodmInvalidIdMemberException : Exception
    {
        // Constructors.
        public MongodmInvalidIdMemberException()
        { }

        public MongodmInvalidIdMemberException(string message)
            : base(message)
        { }

        public MongodmInvalidIdMemberException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
}
