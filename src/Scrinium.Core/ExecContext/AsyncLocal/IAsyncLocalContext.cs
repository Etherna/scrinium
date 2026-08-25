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

namespace Etherna.MongODM.Core.ExecContext.AsyncLocal
{
    /// <summary>
    ///     The <see cref="AsyncLocalContext"/> interface.
    ///     Permits to create an async local context living with the method calling tree.
    /// </summary>
    public interface IAsyncLocalContext : IExecutionContext
    {
        /// <summary>
        /// Initialize a new isolated async local context, replacing any current one.
        /// Disposing the returned handler restores the previous context.
        /// </summary>
        /// <returns>The new context handler</returns>
        IAsyncLocalContextHandler InitAsyncLocalContext();
    }
}
