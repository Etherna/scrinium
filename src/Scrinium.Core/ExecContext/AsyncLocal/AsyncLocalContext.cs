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
using System.Collections.Generic;
using System.Threading;

namespace Etherna.Scrinium.Core.ExecContext.AsyncLocal
{
    /// <summary>
    ///     Async local context implementation. This can be used as singleton or with multiple instances.
    ///     The <see cref="AsyncLocal{T}"/> container permits to have an Item instance inside this
    ///     method calling tree.
    /// </summary>
    /// <remarks>
    ///     Before try to use an async local context, call the method <see cref="InitAsyncLocalContext"/>
    ///     for initialize the <see cref="AsyncLocal{T}"/> container, and receive a context handler.
    ///     Each initialization creates a new isolated context, also when another context is already
    ///     present, for example inherited from an ancestor async flow. After have used, dispose the
    ///     handler for restore the previous context.
    /// </remarks>
    public class AsyncLocalContext : IAsyncLocalContext, IHandledAsyncLocalContext
    {
        // Fields.
        private static readonly AsyncLocal<IDictionary<object, object?>?> asyncLocalContext = new();

        // Properties.
        public IDictionary<object, object?>? Items => asyncLocalContext.Value;

        // Static properties.
        public static IAsyncLocalContext Instance { get; } = new AsyncLocalContext();

        // Methods.
        public IAsyncLocalContextHandler InitAsyncLocalContext()
        {
            var parentItems = asyncLocalContext.Value;
            asyncLocalContext.Value = new Dictionary<object, object?>();

            return new AsyncLocalContextHandler(this, parentItems);
        }

        public void OnDisposed(IAsyncLocalContextHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler);

            asyncLocalContext.Value = ((AsyncLocalContextHandler)handler).ParentItems;
        }
    }
}
