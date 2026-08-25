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
using System.Linq;
using System.Linq.Expressions;

namespace Etherna.Scrinium.Core.Extensions
{
    public static class EnumerableExtensions
    {
        /// <summary>
        /// Order and paginate a list of elements
        /// </summary>
        /// <typeparam name="TSource">Source type</typeparam>
        /// <typeparam name="TKey">Ordering key type</typeparam>
        /// <param name="values">Source values</param>
        /// <param name="orderKeySelector">Ordering key selector</param>
        /// <param name="page">Page to take</param>
        /// <param name="take">Elements per page</param>
        /// <returns>Selected elements page</returns>
        /// <exception cref="ArgumentOutOfRangeException">Throw with invalid parameter values</exception>
        public static IEnumerable<TSource> Paginate<TSource, TKey>(
            this IEnumerable<TSource> values,
            Func<TSource, TKey> orderKeySelector,
            int page,
            int take)
        {
            var skip = ComputeSkip(page, take);

            return values.OrderBy(orderKeySelector)
                         .Skip(skip)
                         .Take(take);
        }

        /// <summary>
        /// Order and paginate a list of elements
        /// </summary>
        /// <typeparam name="TSource">Source type</typeparam>
        /// <typeparam name="TKey">Ordering key type</typeparam>
        /// <param name="values">Source values</param>
        /// <param name="orderKeySelector">Ordering key selector</param>
        /// <param name="page">Page to take</param>
        /// <param name="take">Elements per page</param>
        /// <returns>Selected elements page</returns>
        /// <exception cref="ArgumentOutOfRangeException">Throw with invalid parameter values</exception>
        public static IQueryable<TSource> Paginate<TSource, TKey>(
            this IQueryable<TSource> values,
            Expression<Func<TSource, TKey>> orderKeySelector,
            int page,
            int take)
        {
            var skip = ComputeSkip(page, take);

            return values.OrderBy(orderKeySelector)
                         .Skip(skip)
                         .Take(take);
        }

        /// <summary>
        /// Descending order and paginate a list of elements
        /// </summary>
        /// <typeparam name="TSource">Source type</typeparam>
        /// <typeparam name="TKey">Ordering key type</typeparam>
        /// <param name="values">Source values</param>
        /// <param name="orderKeySelector">Ordering key selector</param>
        /// <param name="page">Page to take</param>
        /// <param name="take">Elements per page</param>
        /// <returns>Selected elements page</returns>
        /// <exception cref="ArgumentOutOfRangeException">Throw with invalid parameter values</exception>
        public static IEnumerable<TSource> PaginateDescending<TSource, TKey>(
            this IEnumerable<TSource> values,
            Func<TSource, TKey> orderKeySelector,
            int page,
            int take)
        {
            var skip = ComputeSkip(page, take);

            return values.OrderByDescending(orderKeySelector)
                         .Skip(skip)
                         .Take(take);
        }

        /// <summary>
        /// Descending order and paginate a list of elements
        /// </summary>
        /// <typeparam name="TSource">Source type</typeparam>
        /// <typeparam name="TKey">Ordering key type</typeparam>
        /// <param name="values">Source values</param>
        /// <param name="orderKeySelector">Ordering key selector</param>
        /// <param name="page">Page to take</param>
        /// <param name="take">Elements per page</param>
        /// <returns>Selected elements page</returns>
        /// <exception cref="ArgumentOutOfRangeException">Throw with invalid parameter values</exception>
        public static IQueryable<TSource> PaginateDescending<TSource, TKey>(
            this IQueryable<TSource> values,
            Expression<Func<TSource, TKey>> orderKeySelector,
            int page,
            int take)
        {
            var skip = ComputeSkip(page, take);

            return values.OrderByDescending(orderKeySelector)
                         .Skip(skip)
                         .Take(take);
        }

        // Helpers.
        /// <summary>
        /// Validate paging parameters, and compute the amount of elements to skip before the page
        /// </summary>
        /// <param name="page">Page to take</param>
        /// <param name="take">Elements per page</param>
        /// <returns>Elements to skip</returns>
        /// <exception cref="ArgumentOutOfRangeException">Throw with invalid parameter values</exception>
        private static int ComputeSkip(int page, int take)
        {
            if (page < 0)
                throw new ArgumentOutOfRangeException(nameof(page), page, "Value can't be negative");
            if (take < 1)
                throw new ArgumentOutOfRangeException(nameof(take), take, "Value can't be less than 1");

            /* The elements to skip are the product of the paging parameters, overflowing int with
             * large values: computed in long, an unreachable page fails as an argument error,
             * instead of wrapping to a wrong or negative skip. */
            var skip = (long)page * take;
            if (skip > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(page), page,
                    $"Value with {take} elements per page requires to skip {skip} elements, can't be more than {int.MaxValue}");

            return (int)skip;
        }
    }
}