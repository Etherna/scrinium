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

        /// <summary>
        /// True when the repository denies any write on its collection, index management
        /// included, because required by its own options or by the db context options.
        /// Reads work normally.
        /// </summary>
        bool IsReadOnly { get; }

        Type KeyType { get; }
        Type ModelType { get; }
        string Name { get; }

        Task BuildNewIndexesAsync(
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
        
        Task DeleteOldIndexesAsync(
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
        /// Persist the tracked changes of a model. By default only the changed members are
        /// updated, with a single atomic statement guarded by the current active model map
        /// schema id, and the model is refreshed in place with the returned document state,
        /// including concurrent changes from other scopes: the save is the synchronization
        /// point of the unit of work. Documents serialized with a not active schema are
        /// replaced instead, migrating them, like when the repository requires document
        /// replacement on save. Conflict granularity is the member: concurrent changes to
        /// disjoint members all survive, changes to the same member are last writer wins.
        /// </summary>
        /// <param name="model">The changed model to save</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task SaveChangesAsync(
            IEntityModel model,
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

        Task<TModel> FindOneAsync(
            TKey id,
            CancellationToken cancellationToken = default);

        Task<TModel> FindOneAsync(
            Expression<Func<TModel, bool>> predicate,
            CancellationToken cancellationToken = default);

        Task DeleteAsync(
            TKey id,
            CancellationToken cancellationToken = default);

        Task DeleteAsync(
            TModel model,
            FilterDefinition<TModel>[]? additionalFilters = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Delete from the collection all the documents matching a filter, without resolving
        /// their ids. This is a raw bulk operation: it skips the domain level cleanup of the
        /// model delete, and doesn't touch the changed or loaded models of any db context
        /// scope. Instances already loaded that match the filter stay on their scope, so
        /// finds by id on the same scope keep returning them instead of failing as not found
        /// (remove them explicitly with UnregisterLoadedModel when this matters), but saving
        /// their changes doesn't recreate the deleted documents.
        /// </summary>
        /// <param name="filter">The documents filter</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The number of deleted documents</returns>
        Task<long> DeleteManyAsync(
            Expression<Func<TModel, bool>> filter,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Delete from the collection all the documents matching a filter, without resolving
        /// their ids. This is a raw bulk operation: it skips the domain level cleanup of the
        /// model delete, and doesn't touch the changed or loaded models of any db context
        /// scope. Instances already loaded that match the filter stay on their scope, so
        /// finds by id on the same scope keep returning them instead of failing as not found
        /// (remove them explicitly with UnregisterLoadedModel when this matters), but saving
        /// their changes doesn't recreate the deleted documents.
        /// </summary>
        /// <param name="filter">The documents filter</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The number of deleted documents</returns>
        Task<long> DeleteManyAsync(
            FilterDefinition<TModel> filter,
            CancellationToken cancellationToken = default);

        Task<CreateIndexModel<TModel>[]> GetDefinedIndexModelsAsync();

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

        Task<TModel?> TryFindOneAndAddToSetAsync<TItem>(
            FilterDefinition<TModel> filter,
            Expression<Func<TModel, IEnumerable<TItem>>> setField,
            TItem itemValue,
            FindOneAndUpdateOptions<TModel> options,
            CancellationToken cancellationToken = default);

        Task<TModel?> TryFindOneAndSetFieldAsync<TField>(
            FilterDefinition<TModel> filter,
            Expression<Func<TModel, TField>> field,
            TField fieldValue,
            FindOneAndUpdateOptions<TModel> options,
            CancellationToken cancellationToken = default);

        Task<TModel?> TryFindOneAndUpdateAsync(
            FilterDefinition<TModel> filter,
            UpdateDefinition<TModel> update,
            FindOneAndUpdateOptions<TModel> options,
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

        Task<UpdateResult> UpdateManyAsync(
            Expression<Func<TModel, bool>> filter,
            UpdateDefinition<TModel> update,
            UpdateOptions? updateOptions = null,
            CancellationToken cancellationToken = default);

        Task<UpdateResult> UpdateManyAsync(
            FilterDefinition<TModel> filter,
            UpdateDefinition<TModel> update,
            UpdateOptions? updateOptions = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Find one and modify atomically with an upsert "add to set" operation.
        /// Create a new document if it doesn't exist, add the element to the set if not present, or do nothing if element is already present
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
        /// Create a new document if it doesn't exist, add the element to the set if not present, or do nothing if element is already present
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

        /// <summary>
        /// Find one and modify atomically with an upsert operation.
        /// Create a new document if it doesn't exist, and apply the update on it.
        /// </summary>
        /// <param name="filter">The document find filter</param>
        /// <param name="updateDefinition">The field update definition</param>
        /// <param name="onInsertModel">A new model, in case of insert</param>
        /// <param name="updatedFields">Updated model fields list</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The model as result from find before update</returns>
        Task<TModel?> UpsertAsync(
            FilterDefinition<TModel> filter,
            UpdateDefinition<TModel> updateDefinition,
            TModel onInsertModel,
            FieldDefinition<TModel>[] updatedFields,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Find one and modify atomically with an upsert "increment" operation.
        /// Create a new document if it doesn't exist, increment the field
        /// </summary>
        /// <param name="filter">The document find filter</param>
        /// <param name="incField">The field to increment</param>
        /// <param name="incValue">The increment value</param>
        /// <param name="onInsertModel">A new model, in case of insert</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <typeparam name="TItem">Item type</typeparam>
        /// <returns>The model as result from find before update</returns>
        Task<TModel?> UpsertIncrementAsync<TItem>(
            Expression<Func<TModel, bool>> filter,
            Expression<Func<TModel, TItem>> incField,
            TItem incValue,
            TModel onInsertModel,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Find one and modify atomically with an upsert "add to set" operation.
        /// Create a new document if it doesn't exist, increment the field
        /// </summary>
        /// <param name="filter">The document find filter</param>
        /// <param name="incField">The field to increment</param>
        /// <param name="incValue">The increment value</param>
        /// <param name="onInsertModel">A new model, in case of insert</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <typeparam name="TItem">Item type</typeparam>
        /// <returns>The model as result from find before update</returns>
        Task<TModel?> UpsertIncrementAsync<TItem>(
            FilterDefinition<TModel> filter,
            FieldDefinition<TModel, TItem> incField,
            TItem incValue,
            TModel onInsertModel,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Find one and modify atomically with an upsert "set" operation.
        /// Create a new document if it doesn't exist, set the field
        /// </summary>
        /// <param name="filter">The document find filter</param>
        /// <param name="setField">The field to set</param>
        /// <param name="setValue">The set value</param>
        /// <param name="onInsertModel">A new model, in case of insert</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <typeparam name="TItem">Item type</typeparam>
        /// <returns>The model as result from find before update</returns>
        Task<TModel?> UpsertSetFieldAsync<TItem>(
            Expression<Func<TModel, bool>> filter,
            Expression<Func<TModel, TItem>> setField,
            TItem setValue,
            TModel onInsertModel,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Find one and modify atomically with an upsert "set" operation.
        /// Create a new document if it doesn't exist, set the field
        /// </summary>
        /// <param name="filter">The document find filter</param>
        /// <param name="setField">The field to set</param>
        /// <param name="setValue">The set value</param>
        /// <param name="onInsertModel">A new model, in case of insert</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <typeparam name="TItem">Item type</typeparam>
        /// <returns>The model as result from find before update</returns>
        Task<TModel?> UpsertSetFieldAsync<TItem>(
            FilterDefinition<TModel> filter,
            FieldDefinition<TModel, TItem> setField,
            TItem setValue,
            TModel onInsertModel,
            CancellationToken cancellationToken = default);
    }
}