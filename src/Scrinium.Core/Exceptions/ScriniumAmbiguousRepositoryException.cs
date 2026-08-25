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
    public class ScriniumAmbiguousRepositoryException : Exception
    {
        // Constructors.
        public ScriniumAmbiguousRepositoryException()
        { }

        public ScriniumAmbiguousRepositoryException(string message) : base(message)
        { }

        public ScriniumAmbiguousRepositoryException(string message, Exception innerException) : base(message, innerException)
        { }
    }
}
