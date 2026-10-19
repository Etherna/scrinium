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

using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using System;
using Xunit;

namespace Etherna.Scrinium.Core.Extensions
{
    public class ExecutionContextExtensionsTest
    {
        // Consts.
        private const string Key = "TestKey";

        // Tests.
        [Fact]
        public void FlowLocalStackRegistersOnceAndResolvesTheSameInstance()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var context = AsyncLocalContext.Instance;

            // Action.
            Assert.Null(context.TryGetFlowLocalStack<object>(Key));
            var stack = context.GetOrAddFlowLocalStack<object>(Key);

            // Assert.
            Assert.Same(stack, context.GetOrAddFlowLocalStack<object>(Key));
            Assert.Same(stack, context.TryGetFlowLocalStack<object>(Key));
            Assert.Single(context.Items!);
        }

        [Fact]
        public void ItemsListRegistersOnceAndResolvesTheSameInstance()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var context = AsyncLocalContext.Instance;

            // Action.
            Assert.Null(context.TryGetItemsList<object>(Key));
            var list = context.GetOrAddItemsList<object>(Key);

            // Assert.
            Assert.Same(list, context.GetOrAddItemsList<object>(Key));
            Assert.Same(list, context.TryGetItemsList<object>(Key));
            Assert.Single(context.Items!);
        }

        [Theory]
        [InlineData("an item of another component")]
        [InlineData(null)]
        public void AForeignItemUnderTheKeyIsReportedByEveryAccessor(object? foreignItem)
        {
            /* The items are the ones of the host, like the HttpContext.Items of a request: a
             * key of the library can be taken by another component. The accessors report it,
             * naming the key, instead of casting or resolving null. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var context = AsyncLocalContext.Instance;
            context.Items![Key] = foreignItem;

            // Action and assert.
            foreach (var accessor in new Func<object?>[]
                     {
                         () => context.GetOrAddFlowLocalStack<object>(Key),
                         () => context.TryGetFlowLocalStack<object>(Key),
                         () => context.GetOrAddItemsList<object>(Key),
                         () => context.TryGetItemsList<object>(Key)
                     })
            {
                var exception = Assert.Throws<InvalidOperationException>(accessor);
                Assert.Contains($"'{Key}'", exception.Message, StringComparison.Ordinal);
            }

            //the foreign item is left as it is
            Assert.Same(foreignItem, context.Items![Key]);
        }

        [Fact]
        public void AStackOfAnotherItemTypeUnderTheKeyIsReported()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var context = AsyncLocalContext.Instance;
            context.GetOrAddFlowLocalStack<string>(Key);

            // Action and assert.
            Assert.Throws<InvalidOperationException>(() => context.GetOrAddFlowLocalStack<object>(Key));
            Assert.Throws<InvalidOperationException>(() => context.TryGetFlowLocalStack<object>(Key));
        }
    }
}
