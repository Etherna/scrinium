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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using Xunit;

namespace Etherna.Scrinium.Core.Extensions
{
    public class EnumerableExtensionsTest
    {
        // Fields.
        private readonly Func<int, int> keySelector = value => value;
        private readonly Expression<Func<int, int>> keySelectorExpression = value => value;
        private readonly int[] values = [1, 2, 3, 4, 5, 6];

        // Tests.
        [Fact]
        public void MaxSkipIsAccepted()
        {
            // Setup.
            int[] expectedPage = [];

            // Action.
            //the last reachable page: skipping exactly int.MaxValue elements
            var page = values.Paginate(keySelector, int.MaxValue, 1);
            var queryablePage = values.AsQueryable().Paginate(keySelectorExpression, int.MaxValue, 1);

            // Assert.
            Assert.Equal(expectedPage, page);
            Assert.Equal(expectedPage, queryablePage);
        }

        [Fact]
        public void NegativePageIsRejected()
        {
            // Action.
            var exceptions = CatchPagingErrors(-1, 10);

            // Assert.
            foreach (var exception in exceptions)
            {
                Assert.Equal("page", exception.ParamName);
                Assert.Equal(-1, exception.ActualValue);
                Assert.Contains("can't be negative", exception.Message, StringComparison.Ordinal);
            }
        }

        /* Paging values whose product doesn't fit an int: without a long computation they wrap
         * to a negative skip (rejected by the server), to zero, or to a wrong positive page. */
        [Theory]
        [InlineData(50_000, 50_000)]   //wraps negative
        [InlineData(65_536, 65_536)]   //wraps to zero
        [InlineData(100_000, 100_000)] //wraps to a wrong positive skip
        [InlineData(int.MaxValue, 2)]
        public void OverflowingPagingIsRejected(int page, int take)
        {
            // Setup.
            var expectedSkip = ((long)page * take).ToString(CultureInfo.InvariantCulture);

            // Action.
            var exceptions = CatchPagingErrors(page, take);

            // Assert.
            foreach (var exception in exceptions)
            {
                Assert.Equal("page", exception.ParamName);
                Assert.Equal(page, exception.ActualValue);
                Assert.Contains(expectedSkip, exception.Message, StringComparison.Ordinal);
                Assert.Contains(take.ToString(CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
                Assert.Contains(int.MaxValue.ToString(CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void PagingSelectsTheRequestedPage()
        {
            // Setup.
            int[] expectedPage = [3, 4];
            int[] expectedDescendingPage = [4, 3];

            // Action.
            var page = values.Paginate(keySelector, 1, 2);
            var queryablePage = values.AsQueryable().Paginate(keySelectorExpression, 1, 2);
            var descendingPage = values.PaginateDescending(keySelector, 1, 2);
            var queryableDescendingPage = values.AsQueryable().PaginateDescending(keySelectorExpression, 1, 2);

            // Assert.
            Assert.Equal(expectedPage, page);
            Assert.Equal(expectedPage, queryablePage);
            Assert.Equal(expectedDescendingPage, descendingPage);
            Assert.Equal(expectedDescendingPage, queryableDescendingPage);
        }

        [Fact]
        public void TakeLessThanOneIsRejected()
        {
            // Action.
            var exceptions = CatchPagingErrors(0, 0);

            // Assert.
            foreach (var exception in exceptions)
            {
                Assert.Equal("take", exception.ParamName);
                Assert.Equal(0, exception.ActualValue);
                Assert.Contains("can't be less than 1", exception.Message, StringComparison.Ordinal);
            }
        }

        // Helpers.
        /// <summary>
        /// Invoke every pagination overload with the same paging values, expecting each one to reject them
        /// </summary>
        private IEnumerable<ArgumentOutOfRangeException> CatchPagingErrors(int page, int take) =>
        [
            Assert.Throws<ArgumentOutOfRangeException>(
                () => values.Paginate(keySelector, page, take)),
            Assert.Throws<ArgumentOutOfRangeException>(
                () => values.AsQueryable().Paginate(keySelectorExpression, page, take)),
            Assert.Throws<ArgumentOutOfRangeException>(
                () => values.PaginateDescending(keySelector, page, take)),
            Assert.Throws<ArgumentOutOfRangeException>(
                () => values.AsQueryable().PaginateDescending(keySelectorExpression, page, take))
        ];
    }
}
