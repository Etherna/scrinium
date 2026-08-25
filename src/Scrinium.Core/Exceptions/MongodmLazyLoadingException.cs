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
    /// Thrown on an implicit lazy load when the db context denies them
    /// (<see cref="Options.ReactionMode.Throw"/> on
    /// <see cref="Options.DbContextOptions.ImplicitLazyLoad"/>): a member of a summary model
    /// was read without a preceding explicit preload.
    /// </summary>
    public class MongodmLazyLoadingException : Exception
    {
        // Constructors.
        public MongodmLazyLoadingException()
        { }

        public MongodmLazyLoadingException(string message)
            : base(message)
        { }

        public MongodmLazyLoadingException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
}
