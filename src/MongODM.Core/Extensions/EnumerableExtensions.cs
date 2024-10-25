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

using Etherna.MongoDB.Driver.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Etherna.MongODM.Core.Extensions
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
            if (page < 0)
                throw new ArgumentOutOfRangeException(nameof(page), page, "Value can't be negative");
            if (take < 1)
                throw new ArgumentOutOfRangeException(nameof(take), take, "Value can't be less than 1");

            return values.OrderBy(orderKeySelector)
                         .Skip(page * take)
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
            if (page < 0)
                throw new ArgumentOutOfRangeException(nameof(page), page, "Value can't be negative");
            if (take < 1)
                throw new ArgumentOutOfRangeException(nameof(take), take, "Value can't be less than 1");

            return values.OrderBy(orderKeySelector)
                         .Skip(page * take)
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
            if (page < 0)
                throw new ArgumentOutOfRangeException(nameof(page), page, "Value can't be negative");
            if (take < 1)
                throw new ArgumentOutOfRangeException(nameof(take), take, "Value can't be less than 1");

            return values.OrderByDescending(orderKeySelector)
                         .Skip(page * take)
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
            if (page < 0)
                throw new ArgumentOutOfRangeException(nameof(page), page, "Value can't be negative");
            if (take < 1)
                throw new ArgumentOutOfRangeException(nameof(take), take, "Value can't be less than 1");

            return values.OrderByDescending(orderKeySelector)
                         .Skip(page * take)
                         .Take(take);
        }
    }
}