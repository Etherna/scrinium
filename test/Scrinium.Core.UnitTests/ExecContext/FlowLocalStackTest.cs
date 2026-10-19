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

using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.Core.ExecContext
{
    public class FlowLocalStackTest
    {
        // Fields.
        private readonly FlowLocalStack<object> stack = new();

        // Tests.
        [Fact]
        public async Task ForkedFlowInheritsTheStackAndKeepsItsOwnPushesLocal()
        {
            // Setup.
            var parentItem = new object();
            var childItem = new object();
            stack.Push(parentItem);

            // Action.
            await Task.Run(() =>
            {
                Assert.Same(parentItem, stack.Top);

                stack.Push(childItem);
                Assert.Equal([childItem, parentItem], stack.Items);
            });

            // Assert.
            //the parent flow doesn't see the push of the forked flow
            Assert.Equal([parentItem], stack.Items);
        }

        [Fact]
        public void RemoveOfAMissingItemChangesNothing()
        {
            // Setup.
            var item = new object();
            stack.Push(item);

            // Action.
            stack.Remove(new object());

            // Assert.
            Assert.Equal([item], stack.Items);
        }

        [Fact]
        public void RemoveOfAnInnerItemKeepsTheItemsAbove()
        {
            // Setup.
            var bottomItem = new object();
            var middleItem = new object();
            var topItem = new object();
            stack.Push(bottomItem);
            stack.Push(middleItem);
            stack.Push(topItem);

            // Action.
            stack.Remove(middleItem);

            // Assert.
            Assert.Equal([topItem, bottomItem], stack.Items);
        }

        [Fact]
        public void RemoveOfTheTopRestoresTheItemBelow()
        {
            // Setup.
            var bottomItem = new object();
            var topItem = new object();
            stack.Push(bottomItem);
            stack.Push(topItem);
            Assert.Same(topItem, stack.Top);

            // Action.
            stack.Remove(topItem);

            // Assert.
            Assert.Same(bottomItem, stack.Top);
            stack.Remove(bottomItem);
            Assert.Null(stack.Top);
            Assert.Empty(stack.Items);
        }

        [Fact]
        public async Task SiblingFlowsDontSeeEachOtherPushes()
        {
            // Setup.
            var firstItem = new object();
            var secondItem = new object();
            var firstPushed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondPushed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            // Action.
            //the second flow pushes while the first one holds its item, and the first reads after it
            var firstFlow = Task.Run(async () =>
            {
                stack.Push(firstItem);
                firstPushed.SetResult();
                await secondPushed.Task;
                return stack.Top;
            });
            var secondFlow = Task.Run(async () =>
            {
                await firstPushed.Task;
                stack.Push(secondItem);
                secondPushed.SetResult();
                return stack.Top;
            });

            // Assert.
            Assert.Same(firstItem, await firstFlow);
            Assert.Same(secondItem, await secondFlow);
            Assert.Null(stack.Top);
        }
    }
}
