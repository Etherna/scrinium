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

using System.Collections.Generic;

namespace Etherna.Scrinium.Core.ExecContext
{
    /// <summary>
    /// Represents an execution context, where information can be put and retrieve alongside
    /// the process with a key-value dictionary.
    /// </summary>
    /// <remarks>
    /// An execution context serves a single logical flow (an HTTP request, a job, a startup
    /// operation), and the flows it forks share its <see cref="Items"/>. The db execution
    /// context and session handlers keep their state local to each async flow on top of the
    /// shared items; the other ambient state kept there resolves by nesting inside one flow,
    /// so parallel flows relying on it must each initialize their own context (e.g. with
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
