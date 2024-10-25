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

using Etherna.MongoDB.Driver;
using Etherna.MongODM.Core.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core.Repositories
{
    public interface IRepository : IDbContextInitializable
    {
        IDbContext DbContext { get; }
        Type KeyType { get; }
        Type ModelType { get; }
        string Name { get; }

        Task BuildIndexesAsync(
            CancellationToken cancellationToken = default);

        Task CreateAsync(
            object model,
            CancellationToken cancellationToken = default);

        Task CreateAsync(
            IEnumerable<object> models,
            CancellationToken cancellationToken = default);

        Task DeleteAsync(
            IEntityModel model,
            CancellationToken cancellationToken = default);

        Task<object> FindOneAsync(
            object id,
            CancellationToken cancellationToken = default);

        string ModelIdToString(object model);

        Task ReplaceAsync(
            object model,
            bool updateDependentDocuments = true,
            CancellationToken cancellationToken = default);

        Task ReplaceAsync(
            object model,
            IClientSessionHandle session,
            bool updateDependentDocuments = true,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Try to find a model and don't throw exception if it is not found
        /// </summary>
        /// <param name="id">Model's Id</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The model, null if it doesn't exist</returns>
        Task<object?> TryFindOneAsync(
            object id,
            CancellationToken cancellationToken = default);
    }

    public interface IRepository<TModel, TKey> : IRepository
        where TModel : class, IEntityModel<TKey>
    {
        Task AccessToCollectionAsync(
            Func<IMongoCollection<TModel>, Task> action,
            bool handleImplicitDbExecutionContext = true);

        Task<TResult> AccessToCollectionAsync<TResult>(
            Func<IMongoCollection<TModel>, Task<TResult>> func,
            bool handleImplicitDbExecutionContext = true);

        Task CreateAsync(
            TModel model,
            CancellationToken cancellationToken = default);

        Task CreateAsync(
            IEnumerable<TModel> models,
            CancellationToken cancellationToken = default);

        Task<IAsyncCursor<TProjection>> FindAsync<TProjection>(
            FilterDefinition<TModel> filter,
            FindOptions<TModel, TProjection>? options = null,
            CancellationToken cancellationToken = default);

        Task<TModel> FindOneAndUpdateAsync(
            FilterDefinition<TModel> filter,
            UpdateDefinition<TModel> update,
            FindOneAndUpdateOptions<TModel> options,
            CancellationToken cancellationToken = default);

        Task<TModel> FindOneAsync(
            TKey id,
            CancellationToken cancellationToken = default);

        Task<TModel> FindOneAsync(
            Expression<Func<TModel, bool>> predicate,
            CancellationToken cancellationToken = default);

        Task DeleteAsync(
            TModel model,
            CancellationToken cancellationToken = default);

        Task DeleteAsync(
            TKey id,
            CancellationToken cancellationToken = default);

        Task<TResult> QueryElementsAsync<TResult>(
            Func<IQueryable<TModel>, Task<TResult>> query,
            AggregateOptions? aggregateOptions = null);

        Task<PaginatedEnumerable<TResult>> QueryPaginatedElementsAsync<TResult, TResultKey>(
            Func<IQueryable<TModel>, IQueryable<TResult>> filter,
            Expression<Func<TResult, TResultKey>> orderKeySelector,
            int page,
            int take,
            bool useDescendingOrder = false,
            CancellationToken cancellationToken = default);

        Task ReplaceAsync(
            TModel model,
            bool updateDependentDocuments = true,
            CancellationToken cancellationToken = default);

        Task ReplaceAsync(
            TModel model,
            IClientSessionHandle session,
            bool updateDependentDocuments = true,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Try to find a model and don't throw exception if it is not found
        /// </summary>
        /// <param name="id">Model's Id</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The model, null if it doesn't exist</returns>
        Task<TModel?> TryFindOneAsync(
            TKey id,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Try to find a model and don't throw exception if it is not found
        /// </summary>
        /// <param name="predicate">Model find predicate</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The model, null if it doesn't exist</returns>
        Task<TModel?> TryFindOneAsync(
            Expression<Func<TModel, bool>> predicate,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Find one and modify atomically with an upsert "add to set" operation.
        /// Create a new document if doesn't exists, add the element to the set if not present, or do nothing if element is already present
        /// </summary>
        /// <param name="filter">The document find filter</param>
        /// <param name="setField">The set where add the item</param>
        /// <param name="itemValue">The item to add</param>
        /// <param name="onInsertModel">A new model, in case of insert</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <typeparam name="TItem">Item type</typeparam>
        /// <returns>The model as result from find before update</returns>
        Task<TModel?> UpsertAddToSetAsync<TItem>(
            Expression<Func<TModel, bool>> filter,
            Expression<Func<TModel, IEnumerable<TItem>>> setField,
            TItem itemValue,
            TModel onInsertModel,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Find one and modify atomically with an upsert "add to set" operation.
        /// Create a new document if doesn't exists, add the element to the set if not present, or do nothing if element is already present
        /// </summary>
        /// <param name="filter">The document find filter</param>
        /// <param name="setField">The set where add the item</param>
        /// <param name="itemValue">The item to add</param>
        /// <param name="onInsertModel">A new model, in case of insert</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <typeparam name="TItem">Item type</typeparam>
        /// <returns>The model as result from find before update</returns>
        Task<TModel?> UpsertAddToSetAsync<TItem>(
            FilterDefinition<TModel> filter,
            FieldDefinition<TModel> setField,
            TItem itemValue,
            TModel onInsertModel,
            CancellationToken cancellationToken = default);
    }
}