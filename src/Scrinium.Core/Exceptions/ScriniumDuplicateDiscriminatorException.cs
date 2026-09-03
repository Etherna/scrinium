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
    /// Thrown at engine build when different model types of a db context share a document
    /// discriminator, which defaults to the simple type name: the discriminator written by
    /// one of them would resolve more than one candidate model type at read.
    /// </summary>
    public class ScriniumDuplicateDiscriminatorException : Exception
    {
        // Constructors.
        public ScriniumDuplicateDiscriminatorException()
        { }

        public ScriniumDuplicateDiscriminatorException(string message)
            : base(message)
        { }

        public ScriniumDuplicateDiscriminatorException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
}
