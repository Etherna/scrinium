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
using System.Threading;

namespace Etherna.Scrinium.Core.ExecContext
{
    /// <summary>
    /// A stack of ambient items local to the async flow. An item pushed by a flow is visible to
    /// that flow and to the flows it forks afterwards, never to its sibling flows, and it leaves
    /// with the async method that pushed it when not removed before: the state of the stack
    /// travels with the <see cref="System.Threading.ExecutionContext"/>, like an
    /// <see cref="AsyncLocal{T}"/> value. The stack instance itself is shared by every flow of
    /// the execution context registering it in its items, so that all of them resolve the same
    /// stack: only its state is per flow.
    /// </summary>
    /// <typeparam name="T">The items type</typeparam>
    internal sealed class FlowLocalStack<T>
        where T : class
    {
        // Internal classes.
        private sealed record Node(T Item, Node? Below);

        // Fields.
        private readonly AsyncLocal<Node?> top = new();

        // Properties.
        /// <summary>
        /// The items held by the current flow, from the top of the stack down.
        /// </summary>
        public IEnumerable<T> Items
        {
            get
            {
                for (var node = top.Value; node is not null; node = node.Below)
                    yield return node.Item;
            }
        }

        /// <summary>
        /// The top item of the current flow, null when the flow holds none.
        /// </summary>
        public T? Top => top.Value?.Item;

        // Methods.
        /// <summary>
        /// Push the item on the stack of the current flow.
        /// </summary>
        /// <param name="item">The item to push</param>
        public void Push(T item) => top.Value = new Node(item, top.Value);

        /// <summary>
        /// Remove the item from the stack of the current flow, wherever it is: the items above
        /// it keep their order. Nothing changes when the flow doesn't hold the item.
        /// </summary>
        /// <param name="item">The item to remove</param>
        public void Remove(T item)
        {
            // Find the item, keeping the nodes above it.
            List<Node> nodesAbove = [];
            var node = top.Value;
            while (node is not null && !ReferenceEquals(node.Item, item))
            {
                nodesAbove.Add(node);
                node = node.Below;
            }
            if (node is null) //not held by this flow
                return;

            // Rebuild the nodes above the item, on top of the ones below it.
            var newTop = node.Below;
            for (var i = nodesAbove.Count - 1; i >= 0; i--)
                newTop = new Node(nodesAbove[i].Item, newTop);
            top.Value = newTop;
        }
    }
}
