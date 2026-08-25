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

using Etherna.Scrinium.Core.ExecContext;
using Etherna.Scrinium.Core.ExecContext.Exceptions;
using System;
using System.Collections.Generic;

namespace Etherna.Scrinium.Core.Extensions
{
    /// <summary>
    /// Access helpers for the ambient state lists kept in <see cref="IExecutionContext.Items"/>.
    /// </summary>
    /// <remarks>
    /// The items dictionary is not thread safe, and parallel flows can share it (e.g. parallel
    /// tasks inside one HTTP request all share the request items). Every library access to the
    /// dictionary locks on the dictionary instance itself, so that a concurrent first claim of
    /// a key, or a read concurrent with it, can't corrupt the dictionary.
    /// </remarks>
    internal static class ExecutionContextExtensions
    {
        /// <summary>
        /// Get the ambient state list registered on the context with the given key,
        /// atomically creating and registering it when not present.
        /// </summary>
        /// <exception cref="ExecutionContextNotFoundException">Context items not initialized</exception>
        public static ICollection<T> GetOrAddItemsList<T>(this IExecutionContext context, string key)
        {
            ArgumentNullException.ThrowIfNull(context);

            var items = context.Items ?? throw new ExecutionContextNotFoundException();
            lock (items)
            {
                if (items.TryGetValue(key, out var listObject))
                    return (ICollection<T>)listObject!;

                List<T> list = [];
                items.Add(key, list);
                return list;
            }
        }

        /// <summary>
        /// Try to get the ambient state list registered on the context with the given key.
        /// Null when the context is not initialized, or the key is not registered.
        /// </summary>
        public static ICollection<T>? TryGetItemsList<T>(this IExecutionContext context, string key)
        {
            ArgumentNullException.ThrowIfNull(context);

            var items = context.Items;
            if (items is null)
                return null;

            lock (items)
                return items.TryGetValue(key, out var listObject) ? (ICollection<T>)listObject! : null;
        }
    }
}
