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

using System;

namespace Etherna.Scrinium.Core.Exceptions
{
    /// <summary>
    /// Thrown at engine build when a model map schema of an entity model type maps an
    /// invalid document id member: a member that is not the implicit implementation of the
    /// entity id contract (the typed id addressed by the framework and the persisted
    /// identity must be the same member), or a member whose serializer represents a
    /// composite (an entity id must serialize to a value, and a document valued id would
    /// render inside the repository id filters as an operator expression).
    /// </summary>
    public class ScriniumInvalidIdMemberException : Exception
    {
        // Constructors.
        public ScriniumInvalidIdMemberException()
        { }

        public ScriniumInvalidIdMemberException(string message)
            : base(message)
        { }

        public ScriniumInvalidIdMemberException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
}
