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

namespace Etherna.Scrinium.Core.Utility
{
    /// <summary>
    /// How <see cref="DbOperationLifecycle.TryExecuteAsync"/> ended: the body ran to its end,
    /// or why it didn't.
    /// </summary>
    internal enum DbOperationExecution
    {
        /// <summary>
        /// The body ran to its end, and the operation closed completed or failed by its result.
        /// </summary>
        Executed,

        /// <summary>
        /// The claim of the start couldn't be resumed: the operation closed cancelled without
        /// running anything.
        /// </summary>
        ClaimNotResumed,

        /// <summary>
        /// The operation was already running when its claim resumed, re-executed after the
        /// worker executing it died: it closed failed without running anything.
        /// </summary>
        AlreadyRunning,

        /// <summary>
        /// An abort was requested: the operation closed cancelled, without running anything
        /// when the request preceded its start, or at the next safe point of its body.
        /// </summary>
        Aborted
    }
}
