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
using Etherna.MongODM.Core.Utility;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
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
        private ILogger logger = null!;
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

            this.logger.RepositoryInitialized(Name, dbContext.Engine.Options.DbName);
        }

        // Properties.
        public IDbContext DbContext { get; private set; } = null!;
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
            ArgumentNullException.ThrowIfNull(func);

            // Initialize collection cache.
            _collection ??= DbContext.Engine.GetMongoCollection<TModel>(options.Name);

            // Invoke func into optional implicit execution context.
            DbExecutionContextHandler? dbExecContextHandler = null;
            if (handleImplicitDbExecutionContext)
                dbExecContextHandler = new DbExecutionContextHandler(DbContext, this);

            var result = await func(_collection).ConfigureAwait(false);

            dbExecContextHandler?.Dispose();

            logger.RepositoryAccessedCollection(Name, DbContext.Engine.Options.DbName);

            return result;
        }

        public virtual async Task BuildNewIndexesAsync(CancellationToken cancellationToken = default)
        {
            var definedIndexes = await GetDefinedIndexModelsAsync().ConfigureAwait(false);

            if (definedIndexes.Length != 0)
                await AccessToCollectionAsync(collection =>
                    collection.Indexes.CreateManyAsync(definedIndexes, cancellationToken)).ConfigureAwait(false);
            
            logger.RepositoryBuiltIndexes(Name, DbContext.Engine.Options.DbName);
        }

        public Task CreateAsync(object model, CancellationToken cancellationToken = default) =>
            CreateAsync((TModel)model, cancellationToken);

        public Task CreateAsync(IEnumerable<object> models, CancellationToken cancellationToken = default) =>
            CreateAsync(models.Select(m => (TModel)m), cancellationToken);

        public virtual async Task CreateAsync(IEnumerable<TModel> models, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(models);

            await CreateOnDBAsync(models, cancellationToken).ConfigureAwait(false);

            logger.RepositoryCreatedDocuments(Name, DbContext.Engine.Options.DbName, models.Select(m => m.Id!.ToString()!));

            //capture the change tracking baselines of the created models, so their later changes are saved.
            using (new DbExecutionContextHandler(DbContext))
                foreach (var model in models)
                    if (TrySerializeModelBsonDocument(model) is { } baseline)
                    {
                        DbContext.SetModelBsonDocument(model, baseline);
                        DbContext.SetModelSourceRepository(model, this);
                    }

            await DbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        public virtual async Task CreateAsync(TModel model, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(model);

            await CreateOnDBAsync(model, cancellationToken).ConfigureAwait(false);

            logger.RepositoryCreatedDocument(Name, DbContext.Engine.Options.DbName, model.Id!.ToString()!);

            //capture the change tracking baseline of the created model, so its later changes are saved.
            using (new DbExecutionContextHandler(DbContext))
                if (TrySerializeModelBsonDocument(model) is { } baseline)
                {
                    DbContext.SetModelBsonDocument(model, baseline);
                    DbContext.SetModelSourceRepository(model, this);
                }

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
            ArgumentNullException.ThrowIfNull(model);

            // Unlink dependent models.
            model.DisposeForDelete();
            await DbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Delete model.
            await DeleteOnDBAsync(model, additionalFilters ?? [], cancellationToken).ConfigureAwait(false);

            // Remove from pending changes and loaded models.
            DbContext.RemoveModelTracking(model);
            DbContext.UnregisterLoadedModel(model.Id!, model);

            logger.RepositoryDeletedDocument(Name, DbContext.Engine.Options.DbName, model.Id!.ToString()!);
        }

        public async Task DeleteAsync(IEntityModel model, CancellationToken cancellationToken = default)
        {
            if (model is not TModel castedModel)
                throw new MongodmInvalidEntityTypeException("Invalid model type");
            await DeleteAsync(castedModel, [], cancellationToken).ConfigureAwait(false);
        }

        public Task<long> DeleteManyAsync(
            Expression<Func<TModel, bool>> filter,
            CancellationToken cancellationToken = default) =>
            DeleteManyAsync(new ExpressionFilterDefinition<TModel>(filter), cancellationToken);

        public Task<long> DeleteManyAsync(
            FilterDefinition<TModel> filter,
            CancellationToken cancellationToken = default) =>
            AccessToCollectionAsync(async collection =>
            {
                var result = await collection.DeleteManyAsync(filter, cancellationToken).ConfigureAwait(false);

                logger.RepositoryDeletedDocuments(Name, DbContext.Engine.Options.DbName, result.DeletedCount);

                return result.DeletedCount;
            });

        public async Task DeleteOldIndexesAsync(CancellationToken cancellationToken = default)
        {
            var definedIndexes = await GetDefinedIndexModelsAsync().ConfigureAwait(false);
            
            await AccessToCollectionAsync(async collection =>
            {
                // Get current indexes.
                var currentIndexes = new List<BsonDocument>();
                using (var indexList = await collection.Indexes.ListAsync(cancellationToken).ConfigureAwait(false))
                    while (await indexList.MoveNextAsync(cancellationToken).ConfigureAwait(false))
                        currentIndexes.AddRange(indexList.Current);

                // Remove old indexes.
                foreach (var oldIndex in from index in currentIndexes
                         let indexName = index.GetElement("name").Value.ToString()
                         where indexName != "_id_"
                         where definedIndexes.All(newIndex => newIndex.Options.Name != indexName)
                         select index)
                    await collection.Indexes
                        .DropOneAsync(oldIndex.GetElement("name").Value.ToString(), cancellationToken)
                        .ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        public virtual async Task<IAsyncCursor<TProjection>> FindAsync<TProjection>(
            FilterDefinition<TModel> filter,
            FindOptions<TModel, TProjection>? options = null,
            CancellationToken cancellationToken = default)
        {
            // Create an explicit db execution context. It needs to survive until cursor is alive.
            var dbExecContextHandler = new DbExecutionContextHandler(DbContext, this);

            return await AccessToCollectionAsync(async collection =>
            {
                var resultCursor = await collection.FindAsync(filter, options, cancellationToken).ConfigureAwait(false);
                var wrappedCursor = new AsyncCursorWrapper<TProjection>(resultCursor, dbExecContextHandler);

                logger.RepositoryQueriedCollection(Name, DbContext.Engine.Options.DbName);

                return wrappedCursor;
            }, false).ConfigureAwait(false);
        }

        public async Task<object> FindOneAsync(object id, CancellationToken cancellationToken = default) =>
            await FindOneAsync((TKey)id, cancellationToken).ConfigureAwait(false);

        public virtual Task<TModel> FindOneAsync(
            TKey id,
            CancellationToken cancellationToken = default)
        {
            /* Read through the loaded models of the current scope: a full instance already
             * loaded satisfies the request without a db round trip. Summary instances still
             * go to db, to be upgraded in place with the full document by deserialization. */
            if (DbContext.TryGetLoadedModel(this, id!) is TModel loadedModel &&
                loadedModel is IReferenceable { IsSummary: false })
                return Task.FromResult(loadedModel);

            return FindOneOnDBAsync(id, cancellationToken);
        }

        public Task<TModel> FindOneAsync(
            Expression<Func<TModel, bool>> predicate,
            CancellationToken cancellationToken = default) =>
            FindOneOnDBAsync(predicate, cancellationToken);
        
        public virtual Task<CreateIndexModel<TModel>[]> GetDefinedIndexModelsAsync() =>
            AccessToCollectionAsync(collection =>
            {
                var indexes = new List<CreateIndexModel<TModel>>();

                // Custom indexes.
                indexes.AddRange(options.IndexBuilders.Select(pair =>
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

                    return new CreateIndexModel<TModel>(keys, options);
                }));

                // By referenced documents.
                var idMemberMaps = DbContext.Engine.MapRegistry.TryGetModelMap(typeof(TModel), out var modelMap) ?
                    modelMap.AllDescendingMemberMaps.Where(mm => mm is { IsEntityReferenceMember: true, IsIdMember: true }) :
                    [];

                var idPaths = idMemberMaps
                    .Select(mm => string.Join(".", mm.MemberMapPath.Select(pathMM => pathMM.BsonMemberMap.ElementName)))
                    .Distinct();

                indexes.AddRange(idPaths.Select(path =>
                    new CreateIndexModel<TModel>(
                        Builders<TModel>.IndexKeys.Ascending(path),
                        new CreateIndexOptions<TModel>
                        {
                            Name = $"ref_{path}",
                            Sparse = true
                        })));

                return Task.FromResult(indexes.ToArray());
            });

        public string ModelIdToString(object model)
        {
            ArgumentNullException.ThrowIfNull(model);
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
                ArgumentNullException.ThrowIfNull(query);

                var result = query(collection.AsQueryable(aggregateOptions));

                logger.RepositoryQueriedCollection(Name, DbContext.Engine.Options.DbName);

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

        public async Task SaveChangesAsync(
            IEntityModel model,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(model);
            if (model is not TModel castedModel)
                throw new MongodmInvalidEntityTypeException("Invalid model type");

            var baseline = DbContext.TryGetModelBsonDocument(model);
            if (baseline is null)
            {
                //no baseline to diff against: persist with a whole document replace.
                logger.RepositorySaveFellBackToDocumentReplace(Name, DbContext.Engine.Options.DbName, castedModel.Id!.ToString()!, "model is not change tracked");
                await ReplaceAsync(castedModel, cancellationToken: cancellationToken).ConfigureAwait(false);
                return;
            }

            // Build the changed members update diffing the model against its baseline.
            /* Members serialization needs the ambient db execution context, like the
             * documents serialization inside collection accesses. */
            using var dbExecutionContext = new DbExecutionContextHandler(DbContext);

            var activeSchema = DbContext.Engine.MapRegistry.GetModelMap(
                DbContext.Engine.ProxyGenerator.PurgeProxyType(model.GetType())).ActiveSchema;

            /* The members safe to diff are all the mapped ones for a full model, but only the
             * loaded ones for a summary: reading an unloaded summary member would trigger its
             * lazy load, and it can't have changed anyway. The extra elements bag is excluded:
             * it isn't a single mapped element, and change tracking never covered it. */
            var extraElementsMemberMap = activeSchema.ExtraElementsMemberMap;
            var membersToDiff = model is IReferenceable { IsSummary: true } summaryModel
                ? activeSchema.AllMemberMaps.Where(mm => summaryModel.SettedMemberNames.Contains(mm.MemberName))
                : activeSchema.AllMemberMaps;
            if (extraElementsMemberMap is not null)
                membersToDiff = membersToDiff.Where(mm => !ReferenceEquals(mm, extraElementsMemberMap));

            //reading the model members to diff them must not flag it a change candidate.
            var changedMembers = new List<MemberInfo>();
            var setDocument = new BsonDocument();
            var unsetDocument = new BsonDocument();
            using (DbContext.SuppressChangeTracking())
                foreach (var memberMap in membersToDiff)
                {
                    var memberValue = memberMap.Getter(castedModel);
                    var currentValue = memberMap.ShouldSerialize(castedModel, memberValue)
                        ? SerializeMemberValue(memberMap, memberValue)
                        : null;
                    var baselineValue = baseline.TryGetValue(memberMap.ElementName, out var bv) ? bv : null;

                    // Skip unchanged members.
                    if (currentValue is null ? baselineValue is null : currentValue.Equals(baselineValue))
                        continue;

                    changedMembers.Add(memberMap.MemberInfo);
                    if (currentValue is not null)
                        setDocument[memberMap.ElementName] = currentValue;
                    else
                        unsetDocument[memberMap.ElementName] = 1;
                }

            var update = new BsonDocument();
            if (setDocument.ElementCount > 0)
                update["$set"] = setDocument;
            if (unsetDocument.ElementCount > 0)
                update["$unset"] = unsetDocument;

            // No change detected: nothing to persist.
            if (update.ElementCount == 0)
            {
                DbContext.ClearChangeCandidate(model);
                logger.RepositorySavedModelChanges(Name, DbContext.Engine.Options.DbName, castedModel.Id!.ToString()!);
                return;
            }

            // Whole document replace when required by the repository options.
            if (options.SaveWithDocumentReplace)
            {
                logger.RepositorySaveFellBackToDocumentReplace(Name, DbContext.Engine.Options.DbName, castedModel.Id!.ToString()!, "document replace is required by repository options");
                await ReplaceAsync(castedModel, cancellationToken: cancellationToken).ConfigureAwait(false);
                return;
            }

            // Update only documents serialized with the current active schema.
            /* The schema check lives in the update filter to be atomic with the update
             * itself: setting members serialized with the active schema into a document
             * shaped by an older schema would mix schemas into a broken document. */
            var filter = Builders<TModel>.Filter.Eq(m => m.Id, castedModel.Id) &
                Builders<TModel>.Filter.Eq(
                    new StringFieldDefinition<TModel, string>(DbContext.Engine.Options.ModelMapVersion.ElementName),
                    activeSchema.Id);

            var updatedModel = await AccessToCollectionAsync(async collection =>
            {
                /* Deserialize the returned document detached from the scope, with the no
                 * cache modifier: the model instance to refresh is already the canonical
                 * one, and deduplication would return it discarding the fresh state. */
                using (DbContext.Engine.SerializerModifierAccessor.EnableCacheSerializerModifier(noCache: true))
                {
                    return await collection.FindOneAndUpdateAsync(
                        filter,
                        new BsonDocumentUpdateDefinition<TModel>(update),
                        new FindOneAndUpdateOptions<TModel> { ReturnDocument = ReturnDocument.After },
                        cancellationToken).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            // Fallback: document not serialized with the active schema, or concurrently deleted.
            /* The whole document replace migrates old schema documents to the active one,
             * and skips silently the deleted documents. */
            if (updatedModel is null)
            {
                logger.RepositorySaveFellBackToDocumentReplace(Name, DbContext.Engine.Options.DbName, castedModel.Id!.ToString()!, "document is not serialized with the active schema, or is deleted");
                await ReplaceAsync(castedModel, cancellationToken: cancellationToken).ConfigureAwait(false);
                return;
            }

            // Refresh the model in place with the updated document state.
            /* A summary model merges the full document, upgrading also its bookkeeping;
             * a full model refreshes all its members: at this point every local change has
             * just been persisted, so only concurrent changes from other scopes can differ. */
            if (model is IReferenceable { IsSummary: true } referenceableModel)
                referenceableModel.MergeFullModel(updatedModel);
            else
                RefreshModel(DbContext, castedModel, updatedModel);

            // Refresh the baseline: the saved model now matches the persisted document.
            if (TrySerializeModelBsonDocument(castedModel) is { } newBaseline)
                DbContext.SetModelBsonDocument(model, newBaseline);

            // Update dependent documents.
            DbContext.Engine.DbMaintainer.OnUpdatedModel<TKey>(castedModel, changedMembers, this);

            // Clear the change candidate.
            DbContext.ClearChangeCandidate(model);

            logger.RepositorySavedModelChanges(Name, DbContext.Engine.Options.DbName, castedModel.Id!.ToString()!);
        }

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
            CancellationToken cancellationToken = default)
        {
            var model = await AccessToCollectionAsync(async collection =>
                await collection.FindOneAndUpdateAsync(filter, update, options, cancellationToken)
                    .ConfigureAwait(false)).ConfigureAwait(false);

            logger.RepositoryFoundAndUpdatedDocument(Name, DbContext.Engine.Options.DbName, model is not null);

            return model;
        }

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
            ArgumentNullException.ThrowIfNull(predicate);

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
            AccessToCollectionAsync<TModel?>(async collection =>
            {
                var serializer = DbContext.Engine.MapRegistry.GetMappedSerializer(typeof(TModel));
                
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
                    .Select(f => f.Render(new((IBsonSerializer<TModel>)serializer, DbContext.Engine.SerializerRegistry)))
                    .Select(f => f.FieldName.Split('.').First())
                    .ToArray();
                var onInsertUpdate = modelBsonDoc[0].AsBsonDocument.Elements
                    .Where(element => element.Name != IdElementName &&          //exclude ID
                                      !skipFieldsNames.Contains(element.Name))  //and fields to skip
                    .Select(element => Builders<TModel>.Update.SetOnInsert(element.Name, element.Value));
                var upsertUpdate = Builders<TModel>.Update.Combine(onInsertUpdate.Append(updateDefinition));

                // Exec on db.
                TModel? oldDocument = await collection.FindOneAndUpdateAsync(filter, upsertUpdate, new FindOneAndUpdateOptions<TModel>
                {
                    IsUpsert = true
                }, cancellationToken).ConfigureAwait(false);
                
                // Detach old document, if present.
                /* The returned old document is a snapshot replaced on db by the upsert: disable
                 * its auditing and remove it from the loaded models, keeping it out of the unit
                 * of work and of next loads deduplication. */
                if (oldDocument is not null)
                {
                    DbContext.RemoveModelTracking(oldDocument);
                    DbContext.UnregisterLoadedModel(oldDocument.Id!, oldDocument);
                }

                logger.RepositoryUpsertedDocument(Name, DbContext.Engine.Options.DbName, oldDocument is null);

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

        // Helpers.
        private static void RefreshModel(IDbContext dbContext, TModel model, TModel updatedModel)
        {
            /* Suppress change tracking on the refresh: the copied members are the just persisted
             * document state, not changes to persist. */
            using (dbContext.SuppressChangeTracking())
            {
                foreach (var member in ReflectionHelper.GetWritableInstanceProperties(typeof(TModel)))
                {
                    var value = ReflectionHelper.GetValue(updatedModel, member);
                    ReflectionHelper.SetValue(model, member, value);
                }
            }
        }

        private static BsonValue SerializeMemberValue(BsonMemberMap memberMap, object? memberValue)
        {
            var document = new BsonDocument();
            using var bsonWriter = new BsonDocumentWriter(document);
            var context = BsonSerializationContext.CreateRoot(bsonWriter);
            bsonWriter.WriteStartDocument();
            bsonWriter.WriteName("value");
            memberMap.GetSerializer().Serialize(context, memberValue);
            bsonWriter.WriteEndDocument();
            return document["value"];
        }

        private BsonDocument? TrySerializeModelBsonDocument(TModel model)
        {
            //an unmapped model can't be serialized, so it can't be change tracked either.
            var serializer = DbContext.Engine.MapRegistry.GetMappedSerializer(typeof(TModel));
            if (serializer is null)
                return null;

            //reading the model members to serialize must not flag it a change candidate.
            var wrapper = new BsonDocument();
            using (DbContext.SuppressChangeTracking())
            using (var bsonWriter = new BsonDocumentWriter(wrapper))
            {
                var context = BsonSerializationContext.CreateRoot(bsonWriter);
                bsonWriter.WriteStartDocument();
                bsonWriter.WriteName("model");
                serializer.Serialize(context, model);
                bsonWriter.WriteEndDocument();
            }
            return wrapper["model"].AsBsonDocument;
        }

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
                ArgumentNullException.ThrowIfNull(model);

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
                ArgumentNullException.ThrowIfNull(predicate);

                using var cursor = await collection.FindAsync(predicate, cancellationToken: cancellationToken).ConfigureAwait(false);
                var model = await cursor.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

                if (model == null)
                    throw new MongodmEntityNotFoundException("Can't find element");

                logger.RepositoryFoundDocument(Name, DbContext.Engine.Options.DbName, model.Id!.ToString()!);

                return model;
            });

        private Task ReplaceHelperAsync(
            TModel model,
            IClientSessionHandle? session,
            bool updateDependentDocuments,
            CancellationToken cancellationToken) =>
            AccessToCollectionAsync(async collection =>
            {
                ArgumentNullException.ThrowIfNull(model);

                // Replace on db.
                ReplaceOneResult result;
                if (session == null)
                {
                    result = await collection.ReplaceOneAsync(
                        Builders<TModel>.Filter.Eq(m => m.Id, model.Id),
                        model,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    result = await collection.ReplaceOneAsync(
                        session,
                        Builders<TModel>.Filter.Eq(m => m.Id, model.Id),
                        model,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                // Update dependent documents.
                /* Skip when the replace matched no document: the model has been deleted
                 * concurrently, like by a bulk delete with filter, and a dependencies
                 * update task would fail reloading it. A whole document replace can change any
                 * member, so all the reference members propagate to their dependent summaries. */
                if (updateDependentDocuments)
                {
                    if (result.MatchedCount > 0)
                    {
                        var activeSchema = DbContext.Engine.MapRegistry.GetModelMap(
                            DbContext.Engine.ProxyGenerator.PurgeProxyType(model.GetType())).ActiveSchema;
                        DbContext.Engine.DbMaintainer.OnUpdatedModel<TKey>(
                            model, activeSchema.AllMemberMaps.Select(mm => mm.MemberInfo), this);
                    }
                    else
                        logger.RepositorySkippedDependenciesUpdate(Name, DbContext.Engine.Options.DbName, model.Id!.ToString()!);
                }

                // Refresh the change tracking: the replaced document is now the model state.
                if (TrySerializeModelBsonDocument(model) is { } newBaseline)
                {
                    DbContext.SetModelBsonDocument(model, newBaseline);
                    DbContext.SetModelSourceRepository(model, this);
                }
                DbContext.ClearChangeCandidate(model);

                logger.RepositoryReplacedDocument(Name, DbContext.Engine.Options.DbName, model.Id!.ToString()!);
            });
    }
}