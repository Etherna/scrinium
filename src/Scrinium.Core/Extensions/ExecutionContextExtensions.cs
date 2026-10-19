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
    /// a key, or a read concurrent with it, can't corrupt the dictionary. The lists are shared
    /// by every flow of the context; a <see cref="FlowLocalStack{T}"/> shares its registration
    /// only, keeping its state local to each flow. The keys live in the items of the host (the
    /// <c>HttpContext.Items</c> of a request): an item of another type under a key of the
    /// library is a registration by another component, reported as such instead of cast.
    /// </remarks>
    internal static class ExecutionContextExtensions
    {
        /// <summary>
        /// Get the flow local stack registered on the context with the given key, atomically
        /// creating and registering it when not present. Every flow of the context resolves the
        /// same stack, whose state is local to each flow.
        /// </summary>
        /// <exception cref="ExecutionContextNotFoundException">Context items not initialized</exception>
        /// <exception cref="InvalidOperationException">The key holds an item of another type</exception>
        public static FlowLocalStack<T> GetOrAddFlowLocalStack<T>(this IExecutionContext context, string key)
            where T : class
        {
            ArgumentNullException.ThrowIfNull(context);

            var items = context.Items ?? throw new ExecutionContextNotFoundException();
            lock (items)
            {
                if (items.TryGetValue(key, out var stackObject))
                    return stackObject as FlowLocalStack<T> ??
                        throw ForeignItemException(key, stackObject, $"the flow local stack of {typeof(T).Name}");

                FlowLocalStack<T> stack = new();
                items.Add(key, stack);
                return stack;
            }
        }

        /// <summary>
        /// Get the ambient state list registered on the context with the given key,
        /// atomically creating and registering it when not present.
        /// </summary>
        /// <exception cref="ExecutionContextNotFoundException">Context items not initialized</exception>
        /// <exception cref="InvalidOperationException">The key holds an item of another type</exception>
        public static ICollection<T> GetOrAddItemsList<T>(this IExecutionContext context, string key)
        {
            ArgumentNullException.ThrowIfNull(context);

            var items = context.Items ?? throw new ExecutionContextNotFoundException();
            lock (items)
            {
                if (items.TryGetValue(key, out var listObject))
                    return listObject as ICollection<T> ??
                        throw ForeignItemException(key, listObject, $"the ambient list of {typeof(T).Name}");

                List<T> list = [];
                items.Add(key, list);
                return list;
            }
        }

        /// <summary>
        /// Try to get the flow local stack registered on the context with the given key.
        /// Null when the context is not initialized, or the key is not registered.
        /// </summary>
        /// <exception cref="InvalidOperationException">The key holds an item of another type</exception>
        public static FlowLocalStack<T>? TryGetFlowLocalStack<T>(this IExecutionContext context, string key)
            where T : class
        {
            ArgumentNullException.ThrowIfNull(context);

            var items = context.Items;
            if (items is null)
                return null;

            lock (items)
            {
                if (!items.TryGetValue(key, out var stackObject))
                    return null;
                return stackObject as FlowLocalStack<T> ??
                    throw ForeignItemException(key, stackObject, $"the flow local stack of {typeof(T).Name}");
            }
        }

        /// <summary>
        /// Try to get the ambient state list registered on the context with the given key.
        /// Null when the context is not initialized, or the key is not registered.
        /// </summary>
        /// <exception cref="InvalidOperationException">The key holds an item of another type</exception>
        public static ICollection<T>? TryGetItemsList<T>(this IExecutionContext context, string key)
        {
            ArgumentNullException.ThrowIfNull(context);

            var items = context.Items;
            if (items is null)
                return null;

            lock (items)
            {
                if (!items.TryGetValue(key, out var listObject))
                    return null;
                return listObject as ICollection<T> ??
                    throw ForeignItemException(key, listObject, $"the ambient list of {typeof(T).Name}");
            }
        }

        // Helpers.
        private static InvalidOperationException ForeignItemException(string key, object? item, string expected) =>
            new($"The execution context item '{key}' holds {(item is null ? "null" : $"a {item.GetType().Name}")} instead of {expected}");
    }
}
