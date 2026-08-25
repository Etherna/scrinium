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

namespace Etherna.Scrinium.Core.ExecContext.AsyncLocal
{
    /// <summary>
    ///     The handler for an <see cref="AsyncLocalContext"/> initialization.
    ///     Dispose this for restore the previous context.
    /// </summary>
    public sealed class AsyncLocalContextHandler : IAsyncLocalContextHandler
    {
        // Fields.
        private bool disposed;

        // Constructors.
        internal AsyncLocalContextHandler(
            IHandledAsyncLocalContext handledContext,
            IDictionary<object, object?>? parentItems)
        {
            HandledContext = handledContext;
            ParentItems = parentItems;
        }

        // Internal properties.
        internal IHandledAsyncLocalContext HandledContext { get; }
        internal IDictionary<object, object?>? ParentItems { get; }

        // Methods.
        public void Dispose()
        {
            if (disposed)
                return;

            HandledContext.OnDisposed(this);
            disposed = true;
        }
    }
}
