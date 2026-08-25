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
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Exceptions;
using Etherna.Scrinium.Core.Extensions;
using Etherna.Scrinium.Core.FieldDefinition;
using Etherna.Scrinium.Core.FilterDefinition;
using Etherna.Scrinium.Core.Repositories;
using Etherna.Scrinium.Core.Serialization.Mapping;
using Etherna.Scrinium.Core.Utility;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Etherna.Scrinium.Core.Tasks
{
    public class UpdateDocDependenciesTask(
        ILogger<UpdateDocDependenciesTask> logger,
        IServiceProvider serviceProvider)
        : IUpdateDocDependenciesTask
    {
        // Methods.
        public async Task RunAsync<TDbContext>(
            Type referencedDbContextType,
            string referencedRepositoryName,
            object referencedModelId,
            IEnumerable<string> idMemberMapIdentifiers)
            where TDbContext : class, IDbContext
        {
            ArgumentNullException.ThrowIfNull(idMemberMapIdentifiers);
            ArgumentNullException.ThrowIfNull(referencedDbContextType);
            ArgumentNullException.ThrowIfNull(referencedModelId);

            logger.UpdateDocDependenciesTaskStarted(typeof(TDbContext), referencedRepositoryName, referencedModelId.ToString()!, idMemberMapIdentifiers);

            // Get dbcontext.
            var dbContext = (TDbContext)serviceProvider.GetService(typeof(TDbContext))!;
            using var dbExecutionContext = new DbExecutionContextHandler(dbContext); //run into a db execution context

            /* The task never holds an exclusive access allowance: executed during an
             * exclusive access (e.g. a migration), its collection accesses throw, and the
             * task executor retries later — background propagation stays out of exclusive
             * works, and converges on their outcome. */

            // Recover the reference id member maps from the map registry.
            /*
             * At this point idMemberMapIdentifiers contains Ids from all reference Member Maps, also from different ModelMaps/Schemas, also ponting to the same Id paths.
             *
             * Verify that member map exists, because a scheduled task could be executed with a different configuration respectly to when it has been generated.
             * This could happen for example if the software is upgraded in the meanwhile.
             */
            /*
             * Skip the id member maps whose element path contains an unknown document key
             * (a dictionary in document representation): the path can't render an update
             * filter, since querying unknown document keys is still not supported by Mongo
             * (https://jira.mongodb.org/browse/SERVER-267), so their summaries stay stale,
             * reported by the engine build warning.
             */
            var recoveredIdMemberMaps = idMemberMapIdentifiers
                .Select(idMemberMapIdentifier => dbContext.Engine.MapRegistry.MemberMapsById.TryGetValue(idMemberMapIdentifier, out var idmm) ? idmm : null!)
                .Where(idMemberMap => idMemberMap is not null)
                .Where(idMemberMap => !idMemberMap.ElementPathHasUndefinedDocumentElement)
                .ToArray();

            // Resolve the referenced repository from the reference serializers of the member maps.
            /* The referenced model can live on this db context, or on a child db context
             * attached to the scope for a cross db context reference: the source repository
             * of the hosting reference serializers resolves on either, like a lazy load
             * would do, verified against the db context type and repository name carried by
             * the payload — repository names are unique per db context only, so a same
             * named repository of another db context never serves the read. A pair that no
             * recovered configuration sources anymore (e.g. after a software upgrade) skips
             * without failing, or the task executor would retry forever a task that can
             * never succeed. */
            var referencedRepository = recoveredIdMemberMaps
                .Select(idMemberMap => idMemberMap.TryFindHostingReferenceSerializer()?.TryResolveSourceRepository(dbContext))
                .FirstOrDefault(repository => repository is not null &&
                    repository.Name == referencedRepositoryName &&
                    repository.DbContext.Engine.DbContextType == referencedDbContextType);
            if (referencedRepository is null)
            {
                logger.UpdateDocDependenciesTaskSkippedOnUnknownRepository(typeof(TDbContext), referencedRepositoryName);
                return;
            }

            /* A model deleted while its update task was pending has nothing to propagate:
             * skip without failing too. The referencing summaries keep their last
             * denormalized values. */
            var referencedModel = await referencedRepository.TryFindOneAsync(referencedModelId).ConfigureAwait(false);
            if (referencedModel is null)
            {
                logger.UpdateDocDependenciesTaskSkippedOnDeletedModel(typeof(TDbContext), referencedRepositoryName, referencedModelId.ToString()!);
                return;
            }
            var referencedModelType = dbContext.Engine.ProxyGenerator.PurgeProxyType(referencedModel.GetType());

            // Select the id member maps of the referenced model type.
            /*
             * We know the referenced model type, and only member maps from the same type are valid. We can select only them.
             */
            var idMemberMaps = recoveredIdMemberMaps
                .Where(idMemberMap => idMemberMap.ModelMapSchema.ModelMap.ModelType == referencedModelType);

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
                    /* A read-only repository consumes documents owned by another application:
                     * their summaries are not this task's to refresh, and every write would
                     * be denied. */
                    .Where(repository => !repository.IsReadOnly)
                    .Where(repository => repository.ModelType.IsAssignableFrom(
                        idmm.MemberMapPath.First().ModelMapSchema.ModelMap.ModelType))
                    .Select(repository => (repository, idmm)))
                .GroupBy(pair => pair.repository, pair => pair.idmm)
                .ToDictionary(repoGroup => repoGroup.Key,
                              repoGroup => repoGroup
                    .Select(idmm =>
                    {
                        /* Select the serializer of the reference member hosting the sub-document,
                         * unwrapping array and dictionary serializers on collection members: the
                         * same serializer writing the summary at document save, so the refreshed
                         * sub-document keeps the reference schema shape, its schema id, and the
                         * discriminator of the current referenced model type. Serializers
                         * reporting themselves as their own item serializer close the walk,
                         * instead of unwrapping containers without end. */
                        var documentSerializer = idmm.ParentMemberMap!.Serializer;
                        HashSet<IBsonSerializer> exploredSerializers = [];
                        while (exploredSerializers.Add(documentSerializer) &&
                               documentSerializer.TryGetContainerChildSerializer(out var childSerializer))
                            documentSerializer = childSerializer;

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

            // Update models.
            /*
             * Update the referencing documents of each repository with a single UpdateMany
             * operation per Id member map path: the server rewrites all the matching
             * documents in one command, and no document content needs to flow back.
             */
            foreach (var repoPair in repositoryDictionary)
            {
                var repository = repoPair.Key;

                var updateManyAsyncMethodInfo = typeof(UpdateDocDependenciesTask)
                    .GetMethod(nameof(UpdateManyAsync), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(repository.ModelType, repository.KeyType);

                foreach (var memberMapPair in repoPair.Value)
                {
                    await ((Task)updateManyAsyncMethodInfo.Invoke(null,
                    [
                        repository,
                        memberMapPair.Key,
                        memberMapPair.Value,
                        referencedModelId
                    ])!).ConfigureAwait(false);
                }
            }

            logger.UpdateDocDependenciesTaskEnded(typeof(TDbContext), referencedRepositoryName, referencedModelId.ToString()!);
        }

        // Helpers.
        private static async Task UpdateManyAsync<TOriginModel, TOriginKey>(
            IRepository<TOriginModel, TOriginKey> repository,
            IMemberMap idMemberMap,
            BsonDocument updatedSubDocument,
            object referencedModelId)
            where TOriginModel : class, IEntityModel<TOriginKey>
        {
            var subDocumentMemberMap = idMemberMap.ParentMemberMap!;

            // Define update filter.
            var filter = new MemberMapEqFilterDefinition<TOriginModel, object>(idMemberMap, referencedModelId);

            // Define update operator.
            var lastUndefinedArrayElement = MemberMapRenderHelper.FindLastUndefinedArrayElement(subDocumentMemberMap);

            var update = Builders<TOriginModel>.Update.Set(
                new MemberMapFieldDefinition<TOriginModel, BsonDocument>(
                    subDocumentMemberMap,
                    MemberMapRenderHelper.BuildArrayFilterFieldSelector(lastUndefinedArrayElement),
                    _ => throw new MongodmElementPathRenderingException("Can't render field with an unknown document key in path"),
                    referToFinalItem: true),
                updatedSubDocument);

            var arrayFilters = new List<ArrayFilterDefinition>();
            if (lastUndefinedArrayElement is not null)
                arrayFilters.Add(new BsonDocumentArrayFilterDefinition<BsonDocument>(
                    new BsonDocument(
                        MemberMapRenderHelper.RenderArrayFilterIdPath(idMemberMap, lastUndefinedArrayElement),
                        new BsonDocument("$eq", updatedSubDocument.GetValue(idMemberMap.BsonMemberMap.ElementName)))));

            // Exec update.
            await repository.UpdateManyAsync(
                filter,
                update,
                new UpdateOptions { ArrayFilters = arrayFilters }).ConfigureAwait(false);
        }
    }
}
