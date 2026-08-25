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

using Moq;
using System.Collections.Generic;
using Xunit;

namespace Etherna.Scrinium.Core.ExecContext
{
    public class ExecutionContextSelectorTests
    {
        [Theory]
        [InlineData(false, false, null)]
        [InlineData(false, true, "1")]
        [InlineData(true, false, "0")]
        [InlineData(true, true, "0")]
        public void ContextSelection(
            bool enableContext1,
            bool enableContext2,
            string? expectedResult)
        {
            // Setup.
            Mock<IExecutionContext> context0 = new();
            Mock<IExecutionContext> context1 = new();
            context0.SetupGet(c => c.Items)
                .Returns(enableContext1 ? new Dictionary<object, object?> { { "val", "0" } } : null);
            context1.SetupGet(c => c.Items)
                .Returns(enableContext2 ? new Dictionary<object, object?> { { "val", "1" } } : null);
            var selector = new ExecutionContextSelector(new[] { context0.Object, context1.Object });

            // Action.
            var result = selector.Items?["val"] as string;

            // Assert.
            Assert.Equal(expectedResult, result);
        }
    }
}
