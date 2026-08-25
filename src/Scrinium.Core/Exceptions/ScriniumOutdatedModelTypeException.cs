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
    /// Thrown interacting with a model instance whose document changed type after the
    /// instance materialized: the runtime type of an instance can't upgrade, so the
    /// instance is invalidated when a load finds the document with another type of its
    /// hierarchy. Reload the model from its repository to get the current type.
    /// </summary>
    public class ScriniumOutdatedModelTypeException : Exception
    {
        // Constructors.
        public ScriniumOutdatedModelTypeException()
        { }

        public ScriniumOutdatedModelTypeException(string message)
            : base(message)
        { }

        public ScriniumOutdatedModelTypeException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
}
