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

namespace Etherna.Scrinium.Core.Exceptions
{
    /// <summary>
    /// Thrown loading the full document of a summary model when the reference denies missing
    /// origin documents (<see cref="Options.ReactionMode.Throw"/> on
    /// <see cref="Serialization.Serializers.ReferenceSerializerConfiguration.MissingOriginDocument"/>):
    /// the referred document doesn't exist anymore on the origin collection, so the summary
    /// can't complete its members.
    /// </summary>
    public class MongodmMissingOriginDocumentException : Exception
    {
        // Constructors.
        public MongodmMissingOriginDocumentException()
        { }

        public MongodmMissingOriginDocumentException(string message)
            : base(message)
        { }

        public MongodmMissingOriginDocumentException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
}
