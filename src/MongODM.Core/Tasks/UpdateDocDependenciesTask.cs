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
using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.Exceptions;
using Etherna.MongODM.Core.Extensions;
using Etherna.MongODM.Core.FieldDefinition;
using Etherna.MongODM.Core.FilterDefinition;
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.Core.Serialization.Mapping;
using Etherna.MongODM.Core.Utility;
using Microsoft.Extensions.Logging;
using MoreLinq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core.Tasks
{
    public class UpdateDocDependenciesTask(
        ILogger<UpdateDocDependenciesTask> logger,
        IServiceProvider serviceProvider)
        : IUpdateDocDependenciesTask
    {
        // Methods.
        public async Task RunAsync<TDbContext>(
            string referencedRepositoryName,
            object referencedModelId,
            IEnumerable<string> idMemberMapIdentifiers)
            where TDbContext : class, IDbContext
        {
            ArgumentNullException.ThrowIfNull(idMemberMapIdentifiers);
            ArgumentNullException.ThrowIfNull(referencedModelId);

            logger.UpdateDocDependenciesTaskStarted(typeof(TDbContext), referencedRepositoryName, referencedModelId.ToString()!, idMemberMapIdentifiers);

            // Get dbcontext.
            var dbContext = (TDbContext)serviceProvider.GetService(typeof(TDbContext))!;
            using var dbExecutionContext = new DbExecutionContextHandler(dbContext); //run into a db execution context

            /* The task never holds an exclusive access allowance: executed during an
             * exclusive access (e.g. a migration), its collection accesses throw, and the
             * task executor retries later — background propagation stays out of exclusive
             * works, and converges on their outcome. */

            // Get data.
            /* A model deleted while its update task was pending has nothing to propagate:
             * skip without failing, or the task executor would retry forever a task that
             * can never succeed. The referencing summaries keep their last denormalized
             * values. */
            var referencedRepository = dbContext.RepositoryRegistry.Repositories.First(r => r.Name == referencedRepositoryName);
            var referencedModel = await referencedRepository.TryFindOneAsync(referencedModelId).ConfigureAwait(false);
            if (referencedModel is null)
            {
                logger.UpdateDocDependenciesTaskSkippedOnDeletedModel(typeof(TDbContext), referencedRepositoryName, referencedModelId.ToString()!);
                return;
            }
            var referencedModelType = dbContext.Engine.ProxyGenerator.PurgeProxyType(referencedModel.GetType());

            // Recover reference id member maps model's schemas, and all model maps.
            /*
             * At this point idMemberMapIdentifiers contains Ids from all reference Member Maps, also from different ModelMaps/Schemas, also ponting to the same Id paths.
             * Anyway, we know the referenced model type, and only member maps from the same type are valid. We can select only them.
             * 
             * Verify that member map exists, because a scheduled task could be executed with a different configuration respectly to when it has been generated.
             * This could happen for example if the software is upgraded in the meanwhile.
             */
            var idMemberMaps = idMemberMapIdentifiers
                .Select(idMemberMapIdentifier => dbContext.Engine.MapRegistry.MemberMapsById.TryGetValue(idMemberMapIdentifier, out var idmm) ? idmm : null!)
                .Where(idMemberMap => idMemberMap is not null && idMemberMap.ModelMapSchema.ModelMap.ModelType == referencedModelType);

            // Define mapping of serialized documents.
            /*
             * We need to create this dictionary map:
             * - repositoryDictionary: repository -> id member map -> serialized document
             * 
             * Each sub-document is serialized with the serializer of its reference member.
             * 
             * Different id paths may share also same serializers. 
             * Because of this, we use an external cache for avoid to serialize multiple times with same serializer.
             */
            var serializedDocumentsCache = new Dictionary<IBsonSerializer, BsonDocument>();
            var repositoryDictionary = idMemberMaps
                .SelectMany(idmm => dbContext.RepositoryRegistry.Repositories
                    .Where(repository => repository.ModelType.IsAssignableFrom(
                        idmm.MemberMapPath.First().ModelMapSchema.ModelMap.ModelType))
                    .Select(repository => (repository, idmm)))
                .GroupBy(pair => pair.repository, pair => pair.idmm)
                .ToDictionary(repoGroup => repoGroup.Key,
                              repoGroup => repoGroup
                    .Select(idmm =>
                    {
                        /* Select the serializer of the reference member hosting the sub-document,
                         * unwrapping array serializers on collection members: the same serializer
                         * writing the summary at document save, so the refreshed sub-document
                         * keeps the reference schema shape, its schema id, and the discriminator
                         * of the current referenced model type. */
                        var documentSerializer = idmm.ParentMemberMap!.Serializer;
                        while (documentSerializer is IBsonArraySerializer arraySerializer &&
                            arraySerializer.TryGetItemSerializationInfo(out var itemSerializationInfo))
                            documentSerializer = itemSerializationInfo.Serializer;

                        //use cache
                        if (!serializedDocumentsCache.TryGetValue(documentSerializer, out BsonDocument? doc))
                        {
                            doc = new BsonDocument();
                            using var bsonWriter = new BsonDocumentWriter(doc);
                            var context = BsonSerializationContext.CreateRoot(bsonWriter);
                            documentSerializer.Serialize(context, referencedModel);
                            serializedDocumentsCache[documentSerializer] = doc;
                        }
                        return (idMemberMap: idmm, doc);
                    })
                    //take one id member map for each generated path. Drop equivalent member maps generated by secondary schemas, but with same path
                    .GroupBy(pair => pair.idMemberMap.RenderElementPath(false, _ => ".$", _ => ".*"))
                    //take idmm with longer active schemas sequence in path.
                    .Select(pathGroup => pathGroup.Aggregate(
                        (default(ValueTuple<IMemberMap, BsonDocument>), -1), //starting value for longest active schema sequence from root
                        (accumulator, newPair) =>
                        {
                            var prevBestLength = accumulator.Item2;
                            var memberMap = newPair.idMemberMap;
                            var activeSchemeSequenceLength = memberMap.MemberMapPath.TakeWhile(mm => mm.ModelMapSchema.IsCurrentActive).Count();
                            return activeSchemeSequenceLength > prevBestLength ?
                                (newPair, activeSchemeSequenceLength) :
                                accumulator;
                        },
                        accumulator => accumulator.Item1))
                    .ToDictionary(pair => pair.Item1, pair => pair.Item2));

            // Find Ids of documents that may need to be updated.
            /*
             * Use all Id paths to find all Ids of existing documents that may need to be updated.
             * Only already existing documents may require an update, so to limit actions on these documents is safe.
             * 
             * This permits to execute FindAndUpdate actions to an enumerable set of documents.
             */
            var updatableDocumentsIdByRepository = new Dictionary<IRepository, IEnumerable<object>>();
            foreach (var repositoryGroup in repositoryDictionary)
            {
                var repository = repositoryGroup.Key;
                var selectedIdMemberMaps = repositoryGroup.Value.Keys;

                var originModelType = repository.ModelType;
                var originIdType = repository.KeyType;

                var result = typeof(UpdateDocDependenciesTask).GetMethod(nameof(FindUpdatableDocumentsIdAsync), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(originModelType, originIdType)
                    .Invoke(null, [repository, selectedIdMemberMaps, referencedModelId]);

                updatableDocumentsIdByRepository.Add(repository, await ((Task<IEnumerable<object>>)result!).ConfigureAwait(false));
            }

            // Update models.
            /*
             * Update one document at time using FindOneAndUpdate.
             * Iterate on repositories, updatable documents, and Id member maps on different paths.
             */
            foreach (var repoPair in repositoryDictionary)
            {
                var repository = repoPair.Key;

                var originModelType = repository.ModelType;
                var originIdType = repository.KeyType;
                var findAndUpdateAsyncMethodInfo = typeof(UpdateDocDependenciesTask)
                    .GetMethod(nameof(FindAndUpdateAsync), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(originModelType, originIdType);

                foreach (var updatableDocumentId in updatableDocumentsIdByRepository[repository])
                {
                    foreach (var memberMapPair in repoPair.Value)
                    {
                        await ((Task<bool>)findAndUpdateAsyncMethodInfo.Invoke(null,
                        [
                            repository,
                            memberMapPair.Key,
                            memberMapPair.Value,
                            updatableDocumentId,
                            referencedModelId
                        ])!).ConfigureAwait(false);
                    }
                }
            }

            logger.UpdateDocDependenciesTaskEnded(typeof(TDbContext), referencedRepositoryName, referencedModelId.ToString()!);
        }

        // Helpers.
        private static async Task<bool> FindAndUpdateAsync<TOriginModel, TOriginKey>(
            IRepository<TOriginModel, TOriginKey> repository,
            IMemberMap idMemberMap,
            BsonDocument updatedSubDocument,
            TOriginKey originModelId,
            object referencedModelId)
            where TOriginModel : class, IEntityModel<TOriginKey>
        {
            /*
             * If id member map has undefined document element in path, we can't build a filter with it.
             * To query document keys with a wildcard is still not supported by Mongo https://jira.mongodb.org/browse/SERVER-267.
             * This case is possibile, for example, with dictionary serialization in document representation.
             */
            if (idMemberMap.ElementPathHasUndefinedDocumentElement)
                return false;

            var subDocumentMemberMap = idMemberMap.ParentMemberMap!;

            // Define find filter.
            var filter = Builders<TOriginModel>.Filter.And(new[]
            {
                Builders<TOriginModel>.Filter.Eq(m => m.Id, originModelId),
                new MemberMapEqFilterDefinition<TOriginModel, object>(idMemberMap, referencedModelId)
            });

            // Define update operator.
            var lastUndefinedArrayElement = subDocumentMemberMap.MemberMapPath
                .SelectMany(mm => mm.InternalElementPath
                    .OfType<ArrayElementRepresentation>()
                    .Where(arrayElement => arrayElement.ItemIndex is null))
                .LastOrDefault();

            var update = Builders<TOriginModel>.Update.Set(
                new MemberMapFieldDefinition<TOriginModel, BsonDocument>(
                    subDocumentMemberMap,
                    undefArrayElement =>
                        undefArrayElement != lastUndefinedArrayElement ? //if isn't the last array element with undefined index
                        ".$[]" :                                         //select all array items
                        ".$[idfilter]",                                  //else, filter in array items
                    _ => throw new MongodmElementPathRenderingException("Can't render field with an unknown document key in path"),
                    referToFinalItem: true),
                updatedSubDocument);

            var arrayFilters = new List<ArrayFilterDefinition>();
            if (lastUndefinedArrayElement is not null)
                arrayFilters.Add(new BsonDocumentArrayFilterDefinition<BsonDocument>(
                    new BsonDocument($"idfilter{string.Join(".",
                        idMemberMap.MemberMapPath
                            .Reverse()
                            .TakeUntil(mm => mm == lastUndefinedArrayElement.MemberMap)
                            .Reverse() //take all final memeber maps in path until we reach the last with undefined array index
                            .Select(mm =>
                            {
                                //if is the last member map, render internal path only after the last undefined array index
                                var internalElementPathToRender = mm.InternalElementPath;
                                if (mm == lastUndefinedArrayElement.MemberMap)
                                    internalElementPathToRender = internalElementPathToRender.Reverse()
                                                                                             .TakeWhile(e => e != lastUndefinedArrayElement)
                                                                                             .Reverse();

                                var renderedInternalElementPath = MemberMapRenderHelper.RenderInternalItemElementPath(
                                    internalElementPathToRender,
                                    _ => throw new MongodmElementPathRenderingException("Can't exist arrays with undefined index here"),
                                    _ => throw new MongodmElementPathRenderingException("Can't render field with an unknown document key in path"));

                                var returnedString = mm != lastUndefinedArrayElement.MemberMap ?
                                    mm.BsonMemberMap.ElementName + renderedInternalElementPath :
                                    renderedInternalElementPath;

                                return returnedString;
                            }))}",
                        new BsonDocument("$eq", updatedSubDocument.GetValue(idMemberMap.BsonMemberMap.ElementName)))));

            // Exec update.
            var model = await repository.AccessToCollectionAsync(collection =>
                collection.FindOneAndUpdateAsync(
                    filter,
                    update,
                    new FindOneAndUpdateOptions<TOriginModel> { ArrayFilters = arrayFilters })).ConfigureAwait(false);

            return model is not null;
        }

        private static async Task<IEnumerable<object>> FindUpdatableDocumentsIdAsync<TOriginModel, TOriginKey>(
            IRepository<TOriginModel, TOriginKey> repository,
            IEnumerable<IMemberMap> idMemberMaps,
            object referencedModelId)
            where TOriginModel : class, IEntityModel<TOriginKey>
        {
            using var cursor = await repository.FindAsync(
                Builders<TOriginModel>.Filter.Or(
                    idMemberMaps.Where(idmm => !idmm.ElementPathHasUndefinedDocumentElement) //clean out unrenderable member map filters
                                .Select(idmm => new MemberMapEqFilterDefinition<TOriginModel, object>(idmm, referencedModelId))),
                new FindOptions<TOriginModel, TOriginModel>
                {
                    NoCursorTimeout = true,
                    Projection = Builders<TOriginModel>.Projection.Include(m => m.Id) //we need only Id
                }).ConfigureAwait(false);

            List<object> ids = new();
            while (await cursor.MoveNextAsync().ConfigureAwait(false))
                ids.AddRange(cursor.Current.Select(m => (object)m.Id!));

            return ids;
        }
    }
}
