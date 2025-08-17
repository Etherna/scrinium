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

using Etherna.MongoDB.Bson;
using Etherna.MongoDB.Bson.IO;
using Etherna.MongoDB.Bson.Serialization;
using Etherna.MongoDB.Driver;
using Etherna.MongoDB.Driver.Linq;
using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.Exceptions;
using Etherna.MongODM.Core.Extensions;
using Etherna.MongODM.Core.ProxyModels;
using Etherna.MongODM.Core.Serialization.Mapping;
using Etherna.MongODM.Core.Utility;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core.Repositories
{
    public class Repository<TModel, TKey>(RepositoryOptions<TModel> options) :
        IRepository<TModel, TKey>
        where TModel : class, IEntityModel<TKey>
    {
        // Consts.
        private const string IdElementName = "_id";
        
        // Fields.
        private ILogger logger = default!;
        private readonly RepositoryOptions<TModel> options = options ?? throw new ArgumentNullException(nameof(options));
        private IMongoCollection<TModel> _collection = null!;

        // Constructors.
        public Repository(string name)
            : this(new RepositoryOptions<TModel>(name))
        { }

        // Initializer.
        public virtual void Initialize(IDbContext dbContext, ILogger logger)
        {
            if (IsInitialized)
                throw new InvalidOperationException("Instance already initialized");
            DbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

            IsInitialized = true;

            this.logger.RepositoryInitialized(Name, dbContext.Options.DbName);
        }

        // Properties.
        public IDbContext DbContext { get; private set; } = default!;
        public Type KeyType => typeof(TKey);
        public Type ModelType => typeof(TModel);
        public bool IsInitialized { get; private set; }
        public string Name => options.Name;

        // Public methods.
        public Task AccessToCollectionAsync(
            Func<IMongoCollection<TModel>, Task> action,
            bool handleImplicitDbExecutionContext = true) =>
            AccessToCollectionAsync(async collection =>
            {
                await action(collection).ConfigureAwait(false);
                return 0;
            }, handleImplicitDbExecutionContext);

        public async Task<TResult> AccessToCollectionAsync<TResult>(
            Func<IMongoCollection<TModel>, Task<TResult>> func,
            bool handleImplicitDbExecutionContext = true)
        {
            ArgumentNullException.ThrowIfNull(func, nameof(func));

            // Initialize collection cache.
            _collection ??= DbContext.Database.GetCollection<TModel>(options.Name);

            // Invoke func into optional implicit execution context.
            DbExecutionContextHandler? dbExecContextHandler = null;
            if (handleImplicitDbExecutionContext)
                dbExecContextHandler = new DbExecutionContextHandler(DbContext);

            var result = await func(_collection).ConfigureAwait(false);

            dbExecContextHandler?.Dispose();

            logger.RepositoryAccessedCollection(Name, DbContext.Options.DbName);

            return result;
        }

        public virtual Task BuildIndexesAsync(CancellationToken cancellationToken = default) =>
            AccessToCollectionAsync(async collection =>
            {
                var newIndexes = new List<(string name, CreateIndexModel<TModel> createIndex)>();

                // Define new indexes.
                //repository defined
                newIndexes.AddRange(options.IndexBuilders.Select(pair =>
                {
                    var (keys, options) = pair;
                    if (options.Name == null)
                    {
                        try
                        {
                            var renderedKeys = keys.Render(new(collection.DocumentSerializer, collection.Settings.SerializerRegistry));
                            options.Name = $"doc_{string.Join("_", renderedKeys.Names)}";
                        }
                        catch (InvalidOperationException)
                        {
                            throw new MongodmIndexBuildingException($"Can't build custom index in collection \"{Name}\"");
                        }
                    }

                    return (options.Name, new CreateIndexModel<TModel>(keys, options));
                }));

                //referenced documents
                var idMemberMaps = DbContext.MapRegistry.TryGetModelMap(typeof(TModel), out IModelMap? modelMap) ?
                    modelMap!.AllDescendingMemberMaps.Where(mm => mm.IsEntityReferenceMember && mm.IsIdMember) :
                    Array.Empty<IMemberMap>();

                var idPaths = idMemberMaps
                    .Select(mm => string.Join(".", mm.MemberMapPath.Select(pathMM => pathMM.BsonMemberMap.ElementName)))
                    .Distinct();

                newIndexes.AddRange(idPaths.Select(path =>
                    ($"ref_{path}",
                     new CreateIndexModel<TModel>(
                        Builders<TModel>.IndexKeys.Ascending(path),
                        new CreateIndexOptions<TModel>
                        {
                            Name = $"ref_{path}",
                            Sparse = true
                        }))));

                // Get current indexes.
                var currentIndexes = new List<BsonDocument>();
                using (var indexList = await collection.Indexes.ListAsync(cancellationToken).ConfigureAwait(false))
                    while (await indexList.MoveNextAsync(cancellationToken).ConfigureAwait(false))
                        currentIndexes.AddRange(indexList.Current);

                // Remove old indexes.
                foreach (var oldIndex in from index in currentIndexes
                                         let indexName = index.GetElement("name").Value.ToString()
                                         where indexName != "_id_"
                                         where !newIndexes.Any(newIndex => newIndex.name == indexName)
                                         select index)
                {
                    await collection.Indexes.DropOneAsync(oldIndex.GetElement("name").Value.ToString(), cancellationToken).ConfigureAwait(false);
                }

                // Build new indexes.
                if (newIndexes.Count != 0)
                    await collection.Indexes.CreateManyAsync(newIndexes.Select(i => i.createIndex), cancellationToken).ConfigureAwait(false);

                logger.RepositoryBuiltIndexes(Name, DbContext.Options.DbName);
            });

        public Task CreateAsync(object model, CancellationToken cancellationToken = default) =>
            CreateAsync((TModel)model, cancellationToken);

        public Task CreateAsync(IEnumerable<object> models, CancellationToken cancellationToken = default) =>
            CreateAsync(models.Select(m => (TModel)m), cancellationToken);

        public virtual async Task CreateAsync(IEnumerable<TModel> models, CancellationToken cancellationToken = default)
        {
            await CreateOnDBAsync(models, cancellationToken).ConfigureAwait(false);

            logger.RepositoryCreatedDocuments(Name, DbContext.Options.DbName, models.Select(m => m.Id!.ToString()!));

            await DbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        public virtual async Task CreateAsync(TModel model, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(model, nameof(model));

            await CreateOnDBAsync(model, cancellationToken).ConfigureAwait(false);

            logger.RepositoryCreatedDocument(Name, DbContext.Options.DbName, model.Id!.ToString()!);

            await DbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task DeleteAsync(TKey id, CancellationToken cancellationToken = default)
        {
            var model = await FindOneAsync(id, cancellationToken: cancellationToken).ConfigureAwait(false);
            await DeleteAsync(model, [], cancellationToken).ConfigureAwait(false);
        }

        public virtual async Task DeleteAsync(
            TModel model,
            FilterDefinition<TModel>[]? additionalFilters = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(model, nameof(model));

            // Unlink dependent models.
            model.DisposeForDelete();
            await DbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Delete model.
            await DeleteOnDBAsync(model, additionalFilters ?? [], cancellationToken).ConfigureAwait(false);

            // Remove from cache.
            if (DbContext.DbCache.LoadedModels.ContainsKey(model.Id!))
                DbContext.DbCache.RemoveModel(model.Id!);

            logger.RepositoryDeletedDocument(Name, DbContext.Options.DbName, model.Id!.ToString()!);
        }

        public async Task DeleteAsync(IEntityModel model, CancellationToken cancellationToken = default)
        {
            if (model is not TModel castedModel)
                throw new MongodmInvalidEntityTypeException("Invalid model type");
            await DeleteAsync(castedModel, [], cancellationToken).ConfigureAwait(false);
        }

        public virtual async Task<IAsyncCursor<TProjection>> FindAsync<TProjection>(
            FilterDefinition<TModel> filter,
            FindOptions<TModel, TProjection>? options = null,
            CancellationToken cancellationToken = default)
        {
            // Create an explicit db execution context. It needs to survive until cursor is alive.
            var dbExecContextHandler = new DbExecutionContextHandler(DbContext);

            return await AccessToCollectionAsync(async collection =>
            {
                var resultCursor = await collection.FindAsync(filter, options, cancellationToken).ConfigureAwait(false);
                var wrappedCursor = new AsyncCursorWrapper<TProjection>(resultCursor, dbExecContextHandler);

                logger.RepositoryQueriedCollection(Name, DbContext.Options.DbName);

                return wrappedCursor;
            }, false).ConfigureAwait(false);
        }

        public async Task<object> FindOneAsync(object id, CancellationToken cancellationToken = default) =>
            await FindOneAsync((TKey)id, cancellationToken).ConfigureAwait(false);

        public virtual async Task<TModel> FindOneAsync(
            TKey id,
            CancellationToken cancellationToken = default)
        {
            if (DbContext.DbCache.LoadedModels.ContainsKey(id!))
            {
                var cachedModel = DbContext.DbCache.LoadedModels[id!] as TModel;
                if (cachedModel is IReferenceable { IsSummary: false })
                    return cachedModel!;
            }

            return await FindOneOnDBAsync(id, cancellationToken).ConfigureAwait(false);
        }

        public Task<TModel> FindOneAsync(
            Expression<Func<TModel, bool>> predicate,
            CancellationToken cancellationToken = default) =>
            FindOneOnDBAsync(predicate, cancellationToken);

        public string ModelIdToString(object model)
        {
            ArgumentNullException.ThrowIfNull(model, nameof(model));
            if (model is not TModel typedModel)
                throw new ArgumentException($"Model is not of {model.GetType().Name} type", nameof(model));
            if (typedModel.Id is null)
                throw new InvalidOperationException("Model Id can't be null");

            return typedModel.Id.ToString()!;
        }

        public virtual Task<TResult> QueryElementsAsync<TResult>(
            Func<IQueryable<TModel>, Task<TResult>> query,
            AggregateOptions? aggregateOptions = null) =>
            AccessToCollectionAsync(collection =>
            {
                ArgumentNullException.ThrowIfNull(query, nameof(query));

                var result = query(collection.AsQueryable(aggregateOptions));

                logger.RepositoryQueriedCollection(Name, DbContext.Options.DbName);

                return result;
            });

        public async Task<PaginatedEnumerable<TResult>> QueryPaginatedElementsAsync<TResult, TResultKey>(
            Func<IQueryable<TModel>, IQueryable<TResult>> filter,
            Expression<Func<TResult, TResultKey>> orderKeySelector,
            int page,
            int take,
            bool useDescendingOrder = false,
            CancellationToken cancellationToken = default)
        {
            var models = await QueryElementsAsync(elements =>

                useDescendingOrder ?

                filter(elements)
                    .PaginateDescending(orderKeySelector, page, take)
                    .ToListAsync(cancellationToken) :

                filter(elements)
                    .Paginate(orderKeySelector, page, take)
                    .ToListAsync(cancellationToken)).ConfigureAwait(false);

            var totalModels = await QueryElementsAsync(elements => filter(elements)
                .LongCountAsync(cancellationToken)).ConfigureAwait(false);

            return new PaginatedEnumerable<TResult>(
                models,
                totalModels,
                page,
                take);
        }

        public virtual Task ReplaceAsync(
            object model,
            bool updateDependentDocuments = true,
            CancellationToken cancellationToken = default) =>
            ReplaceAsync((TModel)model, updateDependentDocuments, cancellationToken);

        public virtual Task ReplaceAsync(
            object model,
            IClientSessionHandle session,
            bool updateDependentDocuments = true,
            CancellationToken cancellationToken = default) =>
            ReplaceAsync((TModel)model, session, updateDependentDocuments, cancellationToken);

        public virtual Task ReplaceAsync(
            TModel model,
            bool updateDependentDocuments = true,
            CancellationToken cancellationToken = default) =>
            ReplaceHelperAsync(model, null, updateDependentDocuments, cancellationToken);

        public virtual Task ReplaceAsync(
            TModel model,
            IClientSessionHandle session,
            bool updateDependentDocuments = true,
            CancellationToken cancellationToken = default) =>
            ReplaceHelperAsync(model, session, updateDependentDocuments, cancellationToken);

        public Task<TModel?> TryFindOneAndAddToSetAsync<TItem>(
            FilterDefinition<TModel> filter,
            Expression<Func<TModel, IEnumerable<TItem>>> setField,
            TItem itemValue,
            FindOneAndUpdateOptions<TModel> options,
            CancellationToken cancellationToken = default) =>
            TryFindOneAndUpdateAsync(
                filter,
                Builders<TModel>.Update.AddToSet(setField, itemValue),
                options,
                cancellationToken);

        public Task<TModel?> TryFindOneAndSetFieldAsync<TField>(
            FilterDefinition<TModel> filter,
            Expression<Func<TModel, TField>> field,
            TField fieldValue,
            FindOneAndUpdateOptions<TModel> options,
            CancellationToken cancellationToken = default) =>
            TryFindOneAndUpdateAsync(
                filter,
                Builders<TModel>.Update.Set(field, fieldValue),
                options,
                cancellationToken);

        public async Task<TModel?> TryFindOneAndUpdateAsync(
            FilterDefinition<TModel> filter,
            UpdateDefinition<TModel> update,
            FindOneAndUpdateOptions<TModel> options,
            CancellationToken cancellationToken = default) =>
            await AccessToCollectionAsync(async collection =>
                await collection.FindOneAndUpdateAsync(filter, update, options, cancellationToken)
                    .ConfigureAwait(false)).ConfigureAwait(false);

        public async Task<object?> TryFindOneAsync(object id, CancellationToken cancellationToken = default) =>
            await TryFindOneAsync((TKey)id, cancellationToken).ConfigureAwait(false);

        public async Task<TModel?> TryFindOneAsync(
            TKey id,
            CancellationToken cancellationToken = default)
        {
            if (id == null)
            {
                return null;
            }

            try
            {
                return await FindOneAsync(id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is FormatException or
                                           MongodmEntityNotFoundException)
            {
                return null;
            }
        }

        public async Task<TModel?> TryFindOneAsync(Expression<Func<TModel, bool>> predicate, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(predicate, nameof(predicate));

            try
            {
                return await FindOneAsync(predicate, cancellationToken).ConfigureAwait(false);
            }
            catch (MongodmEntityNotFoundException)
            {
                return null;
            }
        }

        public Task<UpdateResult> UpdateManyAsync(
            Expression<Func<TModel, bool>> filter,
            UpdateDefinition<TModel> update,
            UpdateOptions? updateOptions = null,
            CancellationToken cancellationToken = default) =>
            UpdateManyAsync(new ExpressionFilterDefinition<TModel>(filter), update, updateOptions, cancellationToken);

        public Task<UpdateResult> UpdateManyAsync(
            FilterDefinition<TModel> filter,
            UpdateDefinition<TModel> update,
            UpdateOptions? updateOptions = null,
            CancellationToken cancellationToken = default) =>
            AccessToCollectionAsync(
                collection => collection.UpdateManyAsync(filter, update, updateOptions, cancellationToken));

        public Task<TModel?> UpsertAddToSetAsync<TItem>(
            Expression<Func<TModel, bool>> filter,
            Expression<Func<TModel, IEnumerable<TItem>>> setField,
            TItem itemValue,
            TModel onInsertModel,
            CancellationToken cancellationToken = default) =>
            UpsertAddToSetAsync(
                new ExpressionFilterDefinition<TModel>(filter),
                new ExpressionFieldDefinition<TModel>(setField),
                itemValue,
                onInsertModel,
                cancellationToken);

        public Task<TModel?> UpsertAddToSetAsync<TItem>(
            FilterDefinition<TModel> filter,
            FieldDefinition<TModel> setField,
            TItem itemValue,
            TModel onInsertModel,
            CancellationToken cancellationToken = default) =>
            UpsertAsync(
                filter,
                Builders<TModel>.Update.AddToSet(setField, itemValue),
                onInsertModel,
                [setField],
                cancellationToken);
        
        public Task<TModel?> UpsertAsync(
            FilterDefinition<TModel> filter,
            UpdateDefinition<TModel> updateDefinition,
            TModel onInsertModel,
            FieldDefinition<TModel>[] updatedFields,
            CancellationToken cancellationToken = default) =>
            AccessToCollectionAsync(async collection =>
            {
                var serializer = DbContext.MapRegistry.GetMappedSerializer(typeof(TModel));
                
                // Serialize model.
                var modelBsonDoc = new BsonDocument();
                using (var bsonWriter = new BsonDocumentWriter(modelBsonDoc))
                {
                    var context = BsonSerializationContext.CreateRoot(bsonWriter);
                    bsonWriter.WriteStartDocument();
                    bsonWriter.WriteName("model");
                    serializer.Serialize(context, onInsertModel);
                    bsonWriter.WriteEndDocument();
                }

                // Update "update" definition with OnInsert instructions.
                var skipFieldsNames = updatedFields
                    .Select(f => f.Render(new((IBsonSerializer<TModel>)serializer, DbContext.SerializerRegistry)))
                    .Select(f => f.FieldName.Split('.').First())
                    .ToArray();
                var onInsertUpdate = modelBsonDoc[0].AsBsonDocument.Elements
                    .Where(element => element.Name != IdElementName &&          //exclude ID
                                      !skipFieldsNames.Contains(element.Name))  //and fields to skip
                    .Select(element => Builders<TModel>.Update.SetOnInsert(element.Name, element.Value));
                var upsertUpdate = Builders<TModel>.Update.Combine(onInsertUpdate.Append(updateDefinition));

                // Exec on db.
                var oldDocument = await collection.FindOneAndUpdateAsync(filter, upsertUpdate, new FindOneAndUpdateOptions<TModel>
                {
                    IsUpsert = true
                }, cancellationToken).ConfigureAwait(false);
                
                // Remove old document from cache, if present.
                if (oldDocument is not null)
                    DbContext.DbCache.RemoveModel(oldDocument.Id!);

                return oldDocument;
            });

        public Task<TModel?> UpsertIncrementAsync<TItem>(
            Expression<Func<TModel, bool>> filter,
            Expression<Func<TModel, TItem>> incField,
            TItem incValue,
            TModel onInsertModel,
            CancellationToken cancellationToken = default) =>
            UpsertIncrementAsync(
                new ExpressionFilterDefinition<TModel>(filter),
                new ExpressionFieldDefinition<TModel, TItem>(incField),
                incValue,
                onInsertModel,
                cancellationToken);

        public Task<TModel?> UpsertIncrementAsync<TItem>(
            FilterDefinition<TModel> filter,
            FieldDefinition<TModel, TItem> incField,
            TItem incValue,
            TModel onInsertModel,
            CancellationToken cancellationToken = default) =>
            UpsertAsync(
                filter,
                Builders<TModel>.Update.Inc(incField, incValue),
                onInsertModel,
                [incField],
                cancellationToken);

        public Task<TModel?> UpsertSetFieldAsync<TItem>(
            Expression<Func<TModel, bool>> filter,
            Expression<Func<TModel, TItem>> setField,
            TItem setValue,
            TModel onInsertModel,
            CancellationToken cancellationToken = default) =>
            UpsertSetFieldAsync(
                new ExpressionFilterDefinition<TModel>(filter),
                new ExpressionFieldDefinition<TModel, TItem>(setField),
                setValue,
                onInsertModel,
                cancellationToken);

        public Task<TModel?> UpsertSetFieldAsync<TItem>(
            FilterDefinition<TModel> filter,
            FieldDefinition<TModel, TItem> setField,
            TItem setValue,
            TModel onInsertModel,
            CancellationToken cancellationToken = default) =>
            UpsertAsync(
                filter,
                Builders<TModel>.Update.Set(setField, setValue),
                onInsertModel,
                [setField],
                cancellationToken);

        // Protected virtual methods.
        protected virtual Task CreateOnDBAsync(IEnumerable<TModel> models, CancellationToken cancellationToken) =>
            AccessToCollectionAsync(collection => collection.InsertManyAsync(models, null, cancellationToken));

        protected virtual Task CreateOnDBAsync(TModel model, CancellationToken cancellationToken) =>
            AccessToCollectionAsync(collection => collection.InsertOneAsync(model, null, cancellationToken));

        protected virtual Task DeleteOnDBAsync(
            TModel model,
            FilterDefinition<TModel>[] additionalFilters,
            CancellationToken cancellationToken) =>
            AccessToCollectionAsync(collection =>
            {
                ArgumentNullException.ThrowIfNull(model, nameof(model));

                var idFilter = Builders<TModel>.Filter.Eq(m => m.Id, model.Id);

                return collection.DeleteOneAsync(
                    additionalFilters.Length == 0 ?
                        idFilter :
                        Builders<TModel>.Filter.And(additionalFilters.Prepend(idFilter)),
                    cancellationToken);
            });

        protected virtual async Task<TModel> FindOneOnDBAsync(TKey id, CancellationToken cancellationToken = default)
        {
            if (id == null)
                throw new ArgumentNullException(nameof(id));

            try
            {
                return await FindOneOnDBAsync(m => m.Id!.Equals(id), cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (MongodmEntityNotFoundException)
            {
                throw new MongodmEntityNotFoundException($"Can't find key {id}");
            }
        }

        // Helpers.
        private Task<TModel> FindOneOnDBAsync(
            Expression<Func<TModel, bool>> predicate,
            CancellationToken cancellationToken = default) =>
            AccessToCollectionAsync(async collection =>
            {
                ArgumentNullException.ThrowIfNull(predicate, nameof(predicate));

                using var cursor = await collection.FindAsync(predicate, cancellationToken: cancellationToken).ConfigureAwait(false);
                var model = await cursor.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

                if (model == default(TModel))
                    throw new MongodmEntityNotFoundException("Can't find element");

                logger.RepositoryFoundDocument(Name, DbContext.Options.DbName, model.Id!.ToString()!);

                return model;
            });

        private Task ReplaceHelperAsync(
            TModel model,
            IClientSessionHandle? session,
            bool updateDependentDocuments,
            CancellationToken cancellationToken) =>
            AccessToCollectionAsync(async collection =>
            {
                ArgumentNullException.ThrowIfNull(model, nameof(model));

                // Replace on db.
                if (session == null)
                {
                    await collection.ReplaceOneAsync(
                        Builders<TModel>.Filter.Eq(m => m.Id, model.Id),
                        model,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await collection.ReplaceOneAsync(
                        session,
                        Builders<TModel>.Filter.Eq(m => m.Id, model.Id),
                        model,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                // Update dependent documents.
                if (updateDependentDocuments)
                    DbContext.DbMaintainer.OnUpdatedModel<TKey>((IAuditable)model);

                // Reset changed members.
                ((IAuditable)model).ResetChangedMembers();

                logger.RepositoryReplacedDocument(Name, DbContext.Options.DbName, model.Id!.ToString()!);
            });
    }
}