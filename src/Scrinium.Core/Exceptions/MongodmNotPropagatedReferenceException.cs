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
    /// Thrown at engine build when a reference id element path contains an unknown document
    /// key (a dictionary in document representation) and the db context declares
    /// <see cref="Options.ReactionMode.Throw"/> on
    /// <see cref="Options.DbContextOptions.NotPropagatedReferences"/>: the dependencies
    /// propagation can't address the path, so its summaries would go stale when the
    /// referenced models change.
    /// </summary>
    public class MongodmNotPropagatedReferenceException : Exception
    {
        // Constructors.
        public MongodmNotPropagatedReferenceException()
        { }

        public MongodmNotPropagatedReferenceException(string message)
            : base(message)
        { }

        public MongodmNotPropagatedReferenceException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
}
