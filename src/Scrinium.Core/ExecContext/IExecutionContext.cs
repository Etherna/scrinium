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

using System.Collections.Generic;

namespace Etherna.Scrinium.Core.ExecContext
{
    /// <summary>
    /// Represents an execution context, where information can be put and retrieve alongside
    /// the process with a key-value dictionary.
    /// </summary>
    /// <remarks>
    /// An execution context serves a single logical flow (an HTTP request, a job, a startup
    /// operation): the ambient state kept in <see cref="Items"/> resolves by nesting inside
    /// one flow. Parallel flows must each initialize their own context (e.g. with
    /// <see cref="AsyncLocal.IAsyncLocalContext.InitAsyncLocalContext"/>), instead of
    /// sharing one.
    /// </remarks>
    public interface IExecutionContext
    {
        /// <summary>
        /// The context dictionary.
        /// </summary>
        IDictionary<object, object?>? Items { get; }
    }
}
