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

namespace Etherna.Scrinium.Core.Options
{
    /// <summary>
    /// How a component reacts to a condition it can tolerate: silently, logging a warning,
    /// or denying the operation with a detailed exception. Each configuration exposing the
    /// mode declares the tolerated condition, the warning deduplication, the thrown
    /// exception, and its own default.
    /// Values are ordered by strictness: where more declarations reach the same target
    /// (like the references materializing the same summary instance), the strictest mode
    /// wins.
    /// </summary>
    public enum ReactionMode
    {
        /// <summary>Tolerate silently.</summary>
        Silent = 0,

        /// <summary>Tolerate, logging a warning.</summary>
        Warn = 1,

        /// <summary>Deny, throwing a detailed exception.</summary>
        Throw = 2
    }
}
