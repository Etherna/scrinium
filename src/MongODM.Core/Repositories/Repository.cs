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
using Etherna.MongODM.Core.FilterDefinition;
using Etherna.MongODM.Core.ProxyModels;
using Etherna.MongODM.Core.Serialization.Mapping;
using Etherna.MongODM.Core.Utility;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core.Repositories
{
    public class Repository<TModel, TKey>(RepositoryOptions<TModel> options) :
        IFullModelsLoader,
        INewReferredModelsCreator,
        IRepository<TModel, TKey>
        where TModel : class, IEntityModel<TKey>
    {
        // Consts.
        private const string IdElementName = "_id";
        /* The driver and the server handle $in lists of some thousands of ids well:
         * chunking bounds the query command size and the materialized result, whatever
         * the caller batch size. */
        private const int LoadFullModelsChunkSize = 1000;
        /* The referenced ids of a missing origin references scan verify their existence
         * with one $in read per chunk, bounded the same way. */
        private const int ScanReferencedIdsChunkSize = 1000;
        
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
        public bool IsInitialized { get; private set; }
        public bool IsReadOnly => options.IsReadOnly || DbContext.Engine.Options.IsReadOnly;
        public Type KeyType => typeof(TKey);
        public Type ModelType => typeof(TModel);
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
            _collection ??= DbContext.Engine.GetMongoCollection<TModel>(options.Name, isReadOnly: options.IsReadOnly);

            // Invoke func into optional implicit execution context.
            /* The handler disposal must run also when func throws: a handler leaked in the
             * flow items would become the ambient one again once the handlers above it
             * complete, resolving the wrong db context and repository for the rest of the flow. */
            using var dbExecContextHandler = handleImplicitDbExecutionContext
                ? new DbExecutionContextHandler(DbContext, this)
                : null;

            var result = await func(_collection).ConfigureAwait(false);

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

        public virtual Task<(IReadOnlyDictionary<string, long> DocumentsBySchemaId, long DocumentsWithoutSchemaId)> CountDocumentsBySchemaIdAsync(
            CancellationToken cancellationToken = default) =>
            AccessToCollectionAsync(async collection =>
            {
                // Read each document schema id from the current element name, or from a read fallback name.
                var schemaIdOptions = DbContext.Engine.Options.ModelMapSchemaId;
                var schemaIdExpression = schemaIdOptions.ReadFallbackElementNames
                    .Prepend(schemaIdOptions.ElementName)
                    .Reverse()
                    .Aggregate(
                        (BsonValue)BsonNull.Value,
                        (fallbackValue, elementName) => new BsonDocument("$ifNull", new BsonArray { "$" + elementName, fallbackValue }));

                var pipeline = PipelineDefinition<TModel, BsonDocument>.Create(
                    new BsonDocument("$group", new BsonDocument
                    {
                        ["_id"] = schemaIdExpression,
                        ["count"] = new BsonDocument("$sum", 1)
                    }));

                var documentsBySchemaId = new Dictionary<string, long>();
                var documentsWithoutSchemaId = 0L;
                using (var cursor = await collection.AggregateAsync(pipeline, cancellationToken: cancellationToken).ConfigureAwait(false))
                {
                    foreach (var group in await cursor.ToListAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var schemaIdValue = group["_id"];
                        var documentsCount = group["count"].ToInt64();
                        if (schemaIdValue.IsBsonNull)
                        {
                            documentsWithoutSchemaId += documentsCount;
                        }
                        else
                        {
                            //accumulate: different bson values could normalize to the same string
                            var schemaId = schemaIdValue.ToString()!;
                            documentsBySchemaId[schemaId] = documentsBySchemaId.TryGetValue(schemaId, out var current) ?
                                current + documentsCount :
                                documentsCount;
                        }
                    }
                }

                logger.RepositoryCountedDocumentsBySchemaId(Name, DbContext.Engine.Options.DbName, documentsBySchemaId.Count);

                return ((IReadOnlyDictionary<string, long>)documentsBySchemaId, documentsWithoutSchemaId);
            });

        public Task CreateAsync(object model, CancellationToken cancellationToken = default) =>
            CreateAsync((TModel)model, cancellationToken);

        public Task CreateAsync(IEnumerable<object> models, CancellationToken cancellationToken = default) =>
            CreateAsync(models.Select(m => (TModel)m), cancellationToken);

        public virtual async Task CreateAsync(IEnumerable<TModel> models, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(models);
            TModel[] modelList = [.. models];

            // Auto create the new referred models, before the insert serializes the references.
            /* The creating model ids are assigned upfront: references back to them from the new
             * referred models serialize complete, cycles between new models included. */
            foreach (var model in modelList)
                TryAssignModelId(model, DbContext.Engine);
            List<(IEntityModel Model, IRepository? SourceRepository)> discoveredNewModels = [];
            foreach (var model in modelList)
                discoveredNewModels.AddRange(DiscoverNewReferredModels(model));
            await CreateNewReferredModelsAsync(discoveredNewModels, modelList, cancellationToken).ConfigureAwait(false);

            await CreateOnDBAsync(modelList, cancellationToken).ConfigureAwait(false);

            logger.RepositoryCreatedDocuments(Name, DbContext.Engine.Options.DbName, modelList.Select(m => m.Id!.ToString()!));

            CaptureCreatedModelsDocuments(modelList);

            await DbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        public virtual async Task CreateAsync(TModel model, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(model);

            // Auto create the new referred models, before the insert serializes the references.
            /* The creating model id is assigned upfront: references back to it from the new
             * referred models serialize complete, cycles between new models included. */
            TryAssignModelId(model, DbContext.Engine);
            await CreateNewReferredModelsAsync(DiscoverNewReferredModels(model), [model], cancellationToken).ConfigureAwait(false);

            await CreateOnDBAsync(model, cancellationToken).ConfigureAwait(false);

            logger.RepositoryCreatedDocument(Name, DbContext.Engine.Options.DbName, model.Id!.ToString()!);

            CaptureCreatedModelsDocuments([model]);

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

            // Delete model.
            await DeleteOnDBAsync(model, additionalFilters ?? [], cancellationToken).ConfigureAwait(false);

            // Remove from pending changes and loaded models.
            DbContext.RemoveModelTracking(model);
            DbContext.UnregisterLoadedModel(model.Id!, model);

            // Propagate the delete to the documents referencing the model.
            DbContext.Engine.DbMaintainer.OnDeletedModel<TKey>(model, this);

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

        public virtual Task<long> EstimatedDocumentCountAsync(CancellationToken cancellationToken = default) =>
            AccessToCollectionAsync(collection =>
                collection.EstimatedDocumentCountAsync(cancellationToken: cancellationToken));

        public virtual async Task<IAsyncCursor<TProjection>> FindAsync<TProjection>(
            FilterDefinition<TModel> filter,
            FindOptions<TModel, TProjection>? options = null,
            CancellationToken cancellationToken = default)
        {
            // Create an explicit db execution context. It needs to survive until cursor is alive.
            var dbExecContextHandler = new DbExecutionContextHandler(DbContext, this);

            try
            {
                return await AccessToCollectionAsync(async collection =>
                {
                    var resultCursor = await collection.FindAsync(filter, options, cancellationToken).ConfigureAwait(false);
                    var wrappedCursor = new AsyncCursorWrapper<TProjection>(resultCursor, dbExecContextHandler);

                    logger.RepositoryQueriedCollection(Name, DbContext.Engine.Options.DbName);

                    return wrappedCursor;
                }, false).ConfigureAwait(false);
            }
            catch
            {
                /* The cursor wrapper owns the handler and disposes it with the cursor:
                 * when the access fails before producing the wrapper, release it here. */
                dbExecContextHandler.Dispose();
                throw;
            }
        }

        public virtual Task<MissingOriginReferencesReport> FindMissingOriginReferencesAsync(
            CancellationToken cancellationToken = default) =>
            AccessToCollectionAsync(async collection =>
            {
                var (scanPaths, unverifiableElementPaths) = BuildReferenceScanPaths();

                var pathReports = new List<MissingOriginReferencesPathReport>();
                foreach (var scanPath in scanPaths)
                {
                    var missingOriginIdsCount = 0L;
                    List<BsonValue> trackedMissingOriginIds = [];
                    await ScanMissingOriginIdsAsync(collection, scanPath, missingOriginIds =>
                    {
                        missingOriginIdsCount += missingOriginIds.Count;
                        trackedMissingOriginIds.AddRange(missingOriginIds.Take(
                            MissingOriginReferencesPathReport.MaxTrackedMissingOriginIds - trackedMissingOriginIds.Count));
                        return Task.CompletedTask;
                    }, cancellationToken).ConfigureAwait(false);

                    // Count the documents carrying a reference to a tracked missing origin id.
                    /* The count addresses the tracked ids: when the tracking cap drops some
                     * of them, it is a lower bound of the documents to repair. */
                    var referencingDocumentsCount = 0L;
                    if (trackedMissingOriginIds.Count > 0)
                        referencingDocumentsCount = await collection.CountDocumentsAsync(
                            new BsonDocument(scanPath.IdElementPath, new BsonDocument("$in", new BsonArray(trackedMissingOriginIds))),
                            cancellationToken: cancellationToken).ConfigureAwait(false);

                    pathReports.Add(new MissingOriginReferencesPathReport(
                        scanPath.ElementPath,
                        scanPath.OriginRepositoryNames,
                        missingOriginIdsCount,
                        trackedMissingOriginIds.Select(id => id.ToString()!).ToArray(),
                        referencingDocumentsCount));
                }

                logger.RepositoryFoundMissingOriginReferences(
                    Name,
                    DbContext.Engine.Options.DbName,
                    pathReports.Sum(pathReport => pathReport.MissingOriginIdsCount));

                return new MissingOriginReferencesReport(pathReports, unverifiableElementPaths);
            });

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
                var customIndexedFields = new HashSet<string>(StringComparer.Ordinal);
                foreach (var (keys, indexOptions) in options.IndexBuilders)
                {
                    BsonDocument renderedKeys;
                    try
                    {
                        renderedKeys = keys.Render(new(collection.DocumentSerializer, collection.Settings.SerializerRegistry));
                    }
                    catch (InvalidOperationException)
                    {
                        throw new MongodmIndexBuildingException($"Can't build custom index in collection \"{Name}\"");
                    }

                    indexOptions.Name ??= $"doc_{string.Join("_", renderedKeys.Names)}";

                    /* An index serves the queries on any left prefix of its keys, and the
                     * automatic indexes have a single key: a custom index opening with that
                     * key already covers them, whatever its following keys and its options.
                     * Only an ascending or descending key counts, being the only one serving
                     * every query shape on its field. */
                    if (renderedKeys.ElementCount > 0 &&
                        renderedKeys.GetElement(0) is { Value.IsNumeric: true } firstKey)
                        customIndexedFields.Add(firstKey.Name);

                    indexes.Add(new CreateIndexModel<TModel>(keys, indexOptions));
                }

                // By referenced documents.
                var idMemberMaps = DbContext.Engine.MapRegistry.TryGetModelMap(typeof(TModel), out var modelMap) ?
                    modelMap.AllDescendingMemberMaps.Where(mm => mm is { IsEntityReferenceMember: true, IsIdMember: true }) :
                    [];

                var idPaths = idMemberMaps
                    .Select(mm => string.Join(".", mm.MemberMapPath.Select(pathMM => pathMM.BsonMemberMap.ElementName)))
                    .Distinct()
                    .Where(path => !customIndexedFields.Contains(path));

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

        public virtual Task<MissingOriginReferencesRemovalReport> RemoveMissingOriginReferencesAsync(
            CancellationToken cancellationToken = default)
        {
            /* Fail fast on a read-only repository: every write on its collection is denied,
             * so a removal could only fail at its first found missing origin reference. */
            if (IsReadOnly)
                throw new UnauthorizedAccessException(
                    $"Can't remove missing origin references from collection \"{Name}\": the repository is read-only");

            return AccessToCollectionAsync(async collection =>
            {
                var (scanPaths, unverifiableElementPaths) = BuildReferenceScanPaths();

                var pathRemovals = new List<MissingOriginReferencesPathRemoval>();
                foreach (var scanPath in scanPaths)
                {
                    var missingOriginIdsCount = 0L;
                    var updatedDocumentsCount = 0L;
                    await ScanMissingOriginIdsAsync(collection, scanPath, async missingOriginIds =>
                    {
                        /* Each update addresses the documents still carrying that missing
                         * origin id at the path: a reference concurrently rewritten to
                         * another document doesn't match anymore, and stays untouched. */
                        foreach (var missingOriginId in missingOriginIds)
                        {
                            var (update, updateOptions) = scanPath.BuildRemoveReferenceUpdate(missingOriginId);
                            var updateResult = await collection.UpdateManyAsync(
                                new BsonDocument(scanPath.IdElementPath, new BsonDocument("$eq", missingOriginId)),
                                update,
                                updateOptions,
                                cancellationToken).ConfigureAwait(false);

                            updatedDocumentsCount += updateResult.ModifiedCount;
                            logger.RepositoryRemovedMissingOriginReference(
                                Name,
                                DbContext.Engine.Options.DbName,
                                scanPath.ElementPath,
                                missingOriginId.ToString()!);
                        }
                        missingOriginIdsCount += missingOriginIds.Count;
                    }, cancellationToken).ConfigureAwait(false);

                    pathRemovals.Add(new MissingOriginReferencesPathRemoval(
                        scanPath.ElementPath,
                        missingOriginIdsCount,
                        updatedDocumentsCount));
                }

                logger.RepositoryRemovedMissingOriginReferences(
                    Name,
                    DbContext.Engine.Options.DbName,
                    pathRemovals.Sum(pathRemoval => pathRemoval.MissingOriginIdsCount),
                    pathRemovals.Sum(pathRemoval => pathRemoval.UpdatedDocumentsCount));

                return new MissingOriginReferencesRemovalReport(pathRemovals, unverifiableElementPaths);
            });
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

            var modelDocument = DbContext.TryGetModelBsonDocument(model);
            if (modelDocument is null)
            {
                //no model document to diff against: persist with a whole document replace.
                logger.RepositorySaveFellBackToDocumentReplace(Name, DbContext.Engine.Options.DbName, castedModel.Id!.ToString()!, "model is not change tracked");
                await ReplaceAsync(castedModel, cancellationToken: cancellationToken).ConfigureAwait(false);
                return;
            }

            // Build the changed members update diffing the model against its model document.
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
            /* The changed members serialization doubles as the new referred models discovery:
             * entity models referred with a null id are new models, created into their source
             * repositories before persisting this update. After a creation the diff recomputes,
             * serializing the references complete with their assigned ids. */
            List<MemberInfo> changedMembers;
            BsonDocument setDocument;
            BsonDocument unsetDocument;
            while (true)
            {
                changedMembers = [];
                setDocument = [];
                unsetDocument = [];

                IReadOnlyCollection<(IEntityModel Model, IRepository? SourceRepository)> discoveredNewModels;
                using (var newModelsCollector = new NewReferredModelsCollector(DbContext.Engine.ExecutionContext))
                {
                    using (DbContext.SuppressChangeTracking())
                        foreach (var memberMap in membersToDiff)
                        {
                            var memberValue = memberMap.Getter(castedModel);
                            var currentValue = memberMap.ShouldSerialize(castedModel, memberValue)
                                ? SerializeMemberValue(memberMap, memberValue)
                                : null;
                            var modelDocumentValue = modelDocument.TryGetValue(memberMap.ElementName, out var bv) ? bv : null;

                            // Skip unchanged members.
                            if (currentValue is null ? modelDocumentValue is null : currentValue.Equals(modelDocumentValue))
                                continue;

                            changedMembers.Add(memberMap.MemberInfo);
                            if (currentValue is not null)
                                setDocument[memberMap.ElementName] = currentValue;
                            else
                                unsetDocument[memberMap.ElementName] = 1;
                        }
                    discoveredNewModels = newModelsCollector.Models;
                }

                if (discoveredNewModels.Count == 0)
                    break;
                await CreateNewReferredModelsAsync(discoveredNewModels, [castedModel], cancellationToken).ConfigureAwait(false);
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
            var filter = new EntityIdEqFilterDefinition<TModel, TKey>(castedModel.Id) &
                Builders<TModel>.Filter.Eq(
                    new StringFieldDefinition<TModel, string>(DbContext.Engine.Options.ModelMapSchemaId.ElementName),
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

            // Refresh the model document: the saved model now matches the persisted document.
            if (TrySerializeModelBsonDocument(castedModel) is { } newModelDocument)
                DbContext.SetModelBsonDocument(model, newModelDocument);

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
                /* The on insert instructions set whole top level elements, so an updated field
                 * inside one of them would make two operators write the same branch, and the
                 * server refuses the update altogether ("Updating the path 'a.b' would create
                 * a conflict at 'a'"): the updated fields are excluded by their first path
                 * segment, the top level element containing them. */
                var skipFieldsNames = updatedFields
                    .Select(f => f.Render(new((IBsonSerializer<TModel>)serializer, DbContext.Engine.SerializerRegistry)))
                    .Select(f => f.FieldName.Split('.').First())
                    .ToArray();
                var onInsertElements = modelBsonDoc[0].AsBsonDocument.Elements
                    .Where(element => element.Name != IdElementName &&          //exclude ID
                                      !skipFieldsNames.Contains(element.Name))  //and fields to skip
                    .ToArray();

                /* The serialized element names compose the $setOnInsert field names verbatim,
                 * and an update field name containing a '.' addresses a nested field: the same
                 * model an insert would write with a literal dotted element, an upsert would
                 * write nested, silently. There is no way to address a literal dotted field in
                 * an update path, so the upsert refuses the model instead of writing another
                 * document than the one it was given. */
                foreach (var element in onInsertElements)
                {
                    if (element.Name.Contains('.', StringComparison.InvariantCulture))
                        throw new InvalidOperationException(
                            $"Can't upsert on collection \"{Name}\": the model of type {typeof(TModel).Name} " +
                            $"serializes the element \"{element.Name}\", and an update field name containing " +
                            "a '.' addresses a nested field, so the upsert would write a document different " +
                            "from the one an insert writes");
                }

                var onInsertUpdate = onInsertElements
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

        // Internals.
        async Task INewReferredModelsCreator.CreateNewReferredModelAsync(IEntityModel model, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(model);
            if (model is not TModel castedModel)
                throw new MongodmInvalidEntityTypeException("Invalid model type");

            await CreateOnDBAsync(castedModel, cancellationToken).ConfigureAwait(false);

            logger.RepositoryCreatedDocument(Name, DbContext.Engine.Options.DbName, castedModel.Id!.ToString()!);

            CaptureCreatedModelsDocuments([castedModel]);
        }

        Task IFullModelsLoader.LoadFullModelsAsync(IEnumerable<IEntityModel> models, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(models);

            var ids = models.Cast<TModel>()
                            .Select(m => m.Id)
                            .Where(id => id is not null)
                            .Distinct()
                            .ToArray();
            if (ids.Length == 0)
                return Task.CompletedTask;

            /* Read the full documents with one query per ids chunk, keeping the $in filter
             * and each materialized result bounded on any caller batch size. Their
             * deserialization runs on the current scope, merging into the loaded summary
             * instances through the identity map. The materialized results are that merge,
             * and don't need to be returned. */
            return AccessToCollectionAsync(async collection =>
            {
                foreach (var idsChunk in ids.Chunk(LoadFullModelsChunkSize))
                    await collection.Find(Builders<TModel>.Filter.In(m => m.Id, idsChunk))
                                    .ToListAsync(cancellationToken).ConfigureAwait(false);
            });
        }

        // Helpers.
        private (IReadOnlyCollection<ReferenceScanPath> ScanPaths, IReadOnlyCollection<string> UnverifiableElementPaths) BuildReferenceScanPaths()
        {
            /* The reference id member maps of every document the collection can store: those
             * of the concrete model types assignable to the repository model type, from every
             * registered schema — a reference written by a since deprecated schema still
             * points to its origin document. */
            var idMemberMaps = DbContext.Engine.MapRegistry.MapsByModelType.Values
                .OfType<IModelMap>()
                .Where(map => !map.ModelType.IsAbstract && typeof(TModel).IsAssignableFrom(map.ModelType))
                .SelectMany(map => map.AllDescendingMemberMaps)
                .Where(memberMap => memberMap is { IsEntityReferenceMember: true, IsIdMember: true });

            var unverifiableElementPaths = new SortedSet<string>(StringComparer.Ordinal);
            List<(string IdElementPath, string ElementPath, string[] UnwindPaths, IMemberMap IdMemberMap, IRepository? OriginRepository)> scanEntries = [];
            foreach (var idMemberMap in idMemberMaps)
            {
                // Walk the element path, planning one unwind per array level.
                /* The id element path is the dotted query path of the referenced ids, arrays
                 * traversed implicitly; the aggregation reading them flattens each array level
                 * with an unwind at its dotted prefix instead. The reference element path is
                 * captured at the reference member's own element name, naming the path in the
                 * reports. */
                string? elementPath = null;
                var idElementPath = new StringBuilder();
                List<string> unwindPaths = [];
                var isVerifiable = true;
                foreach (var pathMemberMap in idMemberMap.MemberMapPath)
                {
                    if (idElementPath.Length > 0)
                        idElementPath.Append('.');
                    idElementPath.Append(pathMemberMap.BsonMemberMap.ElementName);
                    if (pathMemberMap == idMemberMap.ParentMemberMap)
                        elementPath = idElementPath.ToString();

                    foreach (var element in pathMemberMap.InternalElementPath)
                    {
                        switch (element)
                        {
                            case ArrayElementRepresentation { ItemIndex: null }:
                                unwindPaths.Add(idElementPath.ToString());
                                break;
                            case DocumentElementRepresentation { ElementName: { } documentElementName }:
                                idElementPath.Append('.').Append(documentElementName);
                                break;
                            default:
                                /* An unknown document key (a dictionary in document
                                 * representation), and a fixed array position (the value slot
                                 * of an ArrayOfArrays dictionary), can't be addressed by the
                                 * aggregation reading the referenced ids. */
                                isVerifiable = false;
                                break;
                        }
                        if (!isVerifiable)
                            break;
                    }
                    if (!isVerifiable)
                        break;
                }

                if (!isVerifiable)
                {
                    unverifiableElementPaths.Add(idMemberMap.ParentMemberMap!.RenderElementPath(
                        referToFinalItem: false,
                        _ => "",
                        _ => ".*"));
                    continue;
                }

                scanEntries.Add((
                    idElementPath.ToString(),
                    elementPath!,
                    [.. unwindPaths],
                    idMemberMap,
                    idMemberMap.TryFindHostingReferenceSerializer()?.TryResolveSourceRepository(DbContext)));
            }

            // Merge the schemas sharing an id element path into one scan path.
            var scanPaths = new List<ReferenceScanPath>();
            foreach (var pathGroup in scanEntries
                .GroupBy(entry => entry.IdElementPath, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                // A path whose origin repository doesn't resolve can't verify its ids.
                var originRepositories = pathGroup
                    .Select(entry => entry.OriginRepository)
                    .OfType<IRepository>()
                    .Distinct()
                    .OrderBy(repository => repository.Name, StringComparer.Ordinal)
                    .ToArray();
                if (originRepositories.Length == 0)
                {
                    unverifiableElementPaths.Add(pathGroup.First().ElementPath);
                    continue;
                }

                /* Schemas sharing the id element path can wrap different array levels around
                 * it: an unwind passes a non array value through, so the merged plan takes the
                 * highest repetitions count of each prefix, flattening every shape. The
                 * prefixes nest along the path, so their length orders the stages. */
                var unwindCountsByPrefix = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var (_, _, entryUnwindPaths, _, _) in pathGroup)
                    foreach (var prefixGroup in entryUnwindPaths.GroupBy(path => path, StringComparer.Ordinal))
                        unwindCountsByPrefix[prefixGroup.Key] = Math.Max(
                            unwindCountsByPrefix.GetValueOrDefault(prefixGroup.Key, 0),
                            prefixGroup.Count());
                var unwindPaths = unwindCountsByPrefix
                    .OrderBy(pair => pair.Key.Length)
                    .SelectMany(pair => Enumerable.Repeat(pair.Key, pair.Value))
                    .ToArray();

                // The removal shape follows the active schemas when they generate the path.
                var representativeIdMemberMap = pathGroup
                    .OrderByDescending(entry => entry.IdMemberMap.IsGeneratedByActiveSchemas)
                    .First().IdMemberMap;
                var removalShape = ReferenceRemovalShape.TryCreate(representativeIdMemberMap);
                if (removalShape is null)
                {
                    //the walk above already gated these path shapes: keep the report coherent anyway
                    unverifiableElementPaths.Add(pathGroup.First().ElementPath);
                    continue;
                }

                scanPaths.Add(new ReferenceScanPath(
                    elementPath: pathGroup.First().ElementPath,
                    idElementPath: pathGroup.Key,
                    originCollections: originRepositories
                        .Select(repository => repository.DbContext.Engine.GetMongoCollection<BsonDocument>(repository.Name, isReadOnly: true))
                        .ToArray(),
                    originRepositoryNames: originRepositories.Select(repository => repository.Name).ToArray(),
                    removalShape: removalShape,
                    unwindPaths: unwindPaths));
            }

            return (scanPaths, unverifiableElementPaths);
        }

        private void CaptureCreatedModelsDocuments(IEnumerable<TModel> models)
        {
            //capture the model documents of the created models, so their later changes are saved.
            using (new DbExecutionContextHandler(DbContext))
                foreach (var model in models)
                    if (TrySerializeModelBsonDocument(model) is { } modelDocument)
                    {
                        DbContext.SetModelBsonDocument(model, modelDocument);
                        DbContext.SetModelSourceRepository(model, this);
                    }
        }

        private async Task CreateNewReferredModelsAsync(
            IReadOnlyCollection<(IEntityModel Model, IRepository? SourceRepository)> discoveredModels,
            IEnumerable<TModel> persistingModels,
            CancellationToken cancellationToken)
        {
            if (discoveredModels.Count == 0)
                return;

            // Assign every id first, then insert.
            /* With the ids assigned before any insert, references between the new models
             * serialize complete in any creation order, cycles included. Each new model
             * discovers its own referred models in turn, before the inserts. The persisting
             * models are handled by the ongoing operation: they never auto create, also when
             * discovered back from a reference of a new model. */
            var visitedModels = new HashSet<object>(persistingModels, ReferenceEqualityComparer.Instance);
            var modelsToCreate = new List<(IEntityModel Model, IRepository SourceRepository)>();
            var discoveredQueue = new Queue<(IEntityModel Model, IRepository? SourceRepository)>(discoveredModels);
            while (discoveredQueue.Count > 0)
            {
                var (model, sourceRepository) = discoveredQueue.Dequeue();
                if (!visitedModels.Add(model))
                    continue;

                var modelType = DbContext.Engine.ProxyGenerator.PurgeProxyType(model.GetType());
                if (sourceRepository is null)
                    throw new InvalidOperationException(
                        $"Can't auto create the new referred model of type {modelType.Name}: " +
                        "the reference member doesn't resolve a source repository on this db context. " +
                        "Create the model explicitly in its repository before saving");
                if (!TryAssignModelId(model, sourceRepository.DbContext.Engine))
                    throw new InvalidOperationException(
                        $"Can't auto create the new referred model of type {modelType.Name}: " +
                        "its id member doesn't configure an id generator. " +
                        "Create the model explicitly in its repository before saving");

                foreach (var nestedDiscovered in DiscoverNewReferredModels(model))
                    discoveredQueue.Enqueue(nestedDiscovered);

                modelsToCreate.Add((model, sourceRepository));
            }

            foreach (var (model, sourceRepository) in modelsToCreate)
            {
                if (sourceRepository is INewReferredModelsCreator newModelsCreator)
                    await newModelsCreator.CreateNewReferredModelAsync(model, cancellationToken).ConfigureAwait(false);
                else //a custom repository implementation creates with its public api
                    await sourceRepository.CreateAsync(model, cancellationToken).ConfigureAwait(false);
            }

            if (modelsToCreate.Count > 0)
                logger.RepositoryAutoCreatedNewReferredModels(Name, DbContext.Engine.Options.DbName, modelsToCreate.Count);
        }

        private IReadOnlyCollection<(IEntityModel Model, IRepository? SourceRepository)> DiscoverNewReferredModels(
            IEntityModel model)
        {
            /* Serialize the model into a throwaway document with an ambient collector: the
             * reference serializers report every serialized entity model without id, with
             * the source repository resolved for its reference member. */
            var modelType = DbContext.Engine.ProxyGenerator.PurgeProxyType(model.GetType());
            if (!DbContext.Engine.MapRegistry.TryGetMappedSerializer(modelType, out var serializer) ||
                serializer is null)
                return [];

            //reading the model members to serialize must not flag it a change candidate.
            using var newModelsCollector = new NewReferredModelsCollector(DbContext.Engine.ExecutionContext);
            using (new DbExecutionContextHandler(DbContext))
            using (DbContext.SuppressChangeTracking())
            using (var bsonWriter = new BsonDocumentWriter([]))
            {
                var context = BsonSerializationContext.CreateRoot(bsonWriter);
                bsonWriter.WriteStartDocument();
                bsonWriter.WriteName("model");
                serializer.Serialize(context, model);
                bsonWriter.WriteEndDocument();
            }
            return newModelsCollector.Models;
        }

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

        private static async Task ScanMissingOriginIdsAsync(
            IMongoCollection<TModel> collection,
            ReferenceScanPath scanPath,
            Func<IReadOnlyCollection<BsonValue>, Task> onMissingOriginIdsAsync,
            CancellationToken cancellationToken)
        {
            // Read the distinct referenced ids of the path server side.
            /* One unwind per array level flattens the containers (a non array value passes
             * through as a single item), so the group runs on scalar referenced ids and the
             * cursor streams them without materializing the whole set; disk use is allowed,
             * since the distinct ids of a large collection can exceed the memory limit of the
             * group stage. Null ids group with the documents not carrying the path, dropped
             * by the match: a null reference addresses no origin document. */
            List<BsonDocument> stages = [];
            stages.AddRange(scanPath.UnwindPaths.Select(unwindPath => new BsonDocument("$unwind", "$" + unwindPath)));
            stages.Add(new BsonDocument("$group", new BsonDocument("_id", "$" + scanPath.IdElementPath)));
            stages.Add(new BsonDocument("$match", new BsonDocument("_id", new BsonDocument("$ne", BsonNull.Value))));

            var referencedIdsChunk = new List<BsonValue>(ScanReferencedIdsChunkSize);
            using (var referencedIdsCursor = await collection.AggregateAsync(
                PipelineDefinition<TModel, BsonDocument>.Create(stages),
                new AggregateOptions { AllowDiskUse = true },
                cancellationToken).ConfigureAwait(false))
            {
                while (await referencedIdsCursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
                    foreach (var referencedIdGroup in referencedIdsCursor.Current)
                    {
                        referencedIdsChunk.Add(referencedIdGroup["_id"]);
                        if (referencedIdsChunk.Count == ScanReferencedIdsChunkSize)
                        {
                            await ProcessReferencedIdsChunkAsync().ConfigureAwait(false);
                            referencedIdsChunk.Clear();
                        }
                    }
            }
            if (referencedIdsChunk.Count > 0)
                await ProcessReferencedIdsChunkAsync().ConfigureAwait(false);

            async Task ProcessReferencedIdsChunkAsync()
            {
                // Verify the chunk with one $in read per origin collection.
                /* An id found on any origin collection is not missing: with hierarchically
                 * dependent origin repositories, the same path can source from more than one
                 * collection. The ids are compared as stored, without deserializing them. */
                HashSet<BsonValue> missingOriginIds = [.. referencedIdsChunk];
                foreach (var originCollection in scanPath.OriginCollections)
                {
                    if (missingOriginIds.Count == 0)
                        break;

                    using var originIdsCursor = await originCollection.FindAsync(
                        new BsonDocument(IdElementName, new BsonDocument("$in", new BsonArray(missingOriginIds))),
                        new FindOptions<BsonDocument, BsonDocument> { Projection = new BsonDocument(IdElementName, 1) },
                        cancellationToken).ConfigureAwait(false);
                    while (await originIdsCursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
                        foreach (var originDocument in originIdsCursor.Current)
                            missingOriginIds.Remove(originDocument[IdElementName]);
                }

                if (missingOriginIds.Count > 0)
                    await onMissingOriginIdsAsync(missingOriginIds).ConfigureAwait(false);
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

        /* A document written with an id that doesn't render as an addressable filter value
         * couldn't be found, updated or deleted afterwards: build the id filter of the
         * inserting model, refusing the write the same way every other operation refuses
         * to address it, instead of persisting a document reachable only outside MongODM. */
        private void ThrowIfIdIsNotAddressable(TModel model)
        {
            if (!DbContext.Engine.MapRegistry.TryGetMappedSerializer(typeof(TModel), out var modelSerializer))
                return; //without a mapped serializer there is no id serialization to validate

            _ = new EntityIdEqFilterDefinition<TModel, TKey>(model.Id).Render(
                new RenderArgs<TModel>((IBsonSerializer<TModel>)modelSerializer, DbContext.Engine.SerializerRegistry));
        }

        private static bool TryAssignModelId(IEntityModel model, IDbContextEngine engine)
        {
            /* Mirror the driver id assignment of the insert operations: read the id through
             * the id provider of the mapped serializer, and generate it when empty. Returns
             * whether the model has an id after the call. */
            if (!engine.MapRegistry.TryGetMappedSerializer(engine.ProxyGenerator.PurgeProxyType(model.GetType()), out var serializer) ||
                serializer is not IBsonIdProvider idProvider ||
                !idProvider.GetDocumentId(model, out var id, out _, out var idGenerator))
                return false;

            if (!(idGenerator?.IsEmpty(id) ?? id is null))
                return true; //already assigned

            if (idGenerator is null)
                return false;

            idProvider.SetDocumentId(model, idGenerator.GenerateId(container: null!, document: model));
            return true;
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
            AccessToCollectionAsync(collection =>
            {
                foreach (var model in models)
                    ThrowIfIdIsNotAddressable(model);

                return collection.InsertManyAsync(models, null, cancellationToken);
            });

        protected virtual Task CreateOnDBAsync(TModel model, CancellationToken cancellationToken) =>
            AccessToCollectionAsync(collection =>
            {
                ThrowIfIdIsNotAddressable(model);

                return collection.InsertOneAsync(model, null, cancellationToken);
            });

        protected virtual Task DeleteOnDBAsync(
            TModel model,
            FilterDefinition<TModel>[] additionalFilters,
            CancellationToken cancellationToken) =>
            AccessToCollectionAsync(collection =>
            {
                ArgumentNullException.ThrowIfNull(model);

                var idFilter = new EntityIdEqFilterDefinition<TModel, TKey>(model.Id);

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
                return await FindOneOnDBAsync(new EntityIdEqFilterDefinition<TModel, TKey>(id), cancellationToken).ConfigureAwait(false);
            }
            catch (MongodmEntityNotFoundException)
            {
                throw new MongodmEntityNotFoundException($"Can't find key {id}");
            }
        }

        // Helpers.
        private Task<TModel> FindOneOnDBAsync(
            FilterDefinition<TModel> filter,
            CancellationToken cancellationToken = default) =>
            AccessToCollectionAsync(async collection =>
            {
                ArgumentNullException.ThrowIfNull(filter);

                using var cursor = await collection.FindAsync(filter, cancellationToken: cancellationToken).ConfigureAwait(false);
                var model = await cursor.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

                if (model == null)
                    throw new MongodmEntityNotFoundException("Can't find element");

                logger.RepositoryFoundDocument(Name, DbContext.Engine.Options.DbName, model.Id!.ToString()!);

                return model;
            });

        private async Task ReplaceHelperAsync(
            TModel model,
            IClientSessionHandle? session,
            bool updateDependentDocuments,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(model);

            // Auto create the new referred models, before the replace serializes the references.
            await CreateNewReferredModelsAsync(DiscoverNewReferredModels(model), [model], cancellationToken).ConfigureAwait(false);

            await AccessToCollectionAsync(async collection =>
            {
                // Replace on db.
                var idFilter = new EntityIdEqFilterDefinition<TModel, TKey>(model.Id);
                ReplaceOneResult result;
                if (session == null)
                {
                    result = await collection.ReplaceOneAsync(
                        idFilter,
                        model,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    result = await collection.ReplaceOneAsync(
                        session,
                        idFilter,
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
                if (TrySerializeModelBsonDocument(model) is { } newModelDocument)
                {
                    DbContext.SetModelBsonDocument(model, newModelDocument);
                    DbContext.SetModelSourceRepository(model, this);
                }
                DbContext.ClearChangeCandidate(model);

                logger.RepositoryReplacedDocument(Name, DbContext.Engine.Options.DbName, model.Id!.ToString()!);
            }).ConfigureAwait(false);
        }

        // Nested types.
        /* One verifiable reference id element path of the collection documents, with
         * everything precomputed to scan and repair it: the unwinds and the id element path
         * reading the referenced ids, the origin collections verifying their existence, and
         * the removal shape repairing a reference — pulled out of its array when the
         * reference is an array item, set to null otherwise. */
        private sealed class ReferenceScanPath(
            string elementPath,
            string idElementPath,
            IReadOnlyCollection<IMongoCollection<BsonDocument>> originCollections,
            IReadOnlyCollection<string> originRepositoryNames,
            ReferenceRemovalShape removalShape,
            IReadOnlyCollection<string> unwindPaths)
        {
            // Properties.
            public string ElementPath { get; } = elementPath;
            public string IdElementPath { get; } = idElementPath;
            public IReadOnlyCollection<IMongoCollection<BsonDocument>> OriginCollections { get; } = originCollections;
            public IReadOnlyCollection<string> OriginRepositoryNames { get; } = originRepositoryNames;
            public IReadOnlyCollection<string> UnwindPaths { get; } = unwindPaths;

            // Methods.
            public (UpdateDefinition<TModel> Update, UpdateOptions? Options) BuildRemoveReferenceUpdate(BsonValue missingOriginId)
            {
                var (update, arrayFilter) = removalShape.BuildUpdate(missingOriginId);
                return (
                    update,
                    arrayFilter is null ? null : new UpdateOptions
                    {
                        ArrayFilters = [new BsonDocumentArrayFilterDefinition<BsonDocument>(arrayFilter)]
                    });
            }
        }
    }
}