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
using Etherna.MongoDB.Bson.Serialization;
using Etherna.MongoDB.Driver;
using Etherna.MongoDB.Driver.Search;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core.Utility
{
    /* Each operation is implemented once by its session overload, that relaxes the session
     * parameter to nullable: an explicit session is used verbatim, a null one resolves to
     * the ambient session of the engine when active (e.g. a transaction started by
     * ExecuteInTransactionAsync), and without any session the operation runs session-less.
     * The session-less overloads forward with a null session. Change stream watches and
     * estimated document counts stay session-less: they can't run in transactions. */
    [SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix")]
    public class LimitedAccessMongoCollection<TDocument>(
        IDbContextEngine dbContextEngine,
        IMongoCollection<TDocument> mongoCollection,
        bool isReadOnly)
        : IMongoCollection<TDocument>
    {
        // Properties.
        public CollectionNamespace CollectionNamespace
        {
            get
            {
                VerifyReadPermission();
                return mongoCollection.CollectionNamespace;
            }
        }
        public IMongoDatabase Database
        {
            get
            {
                VerifyReadPermission();
                return mongoCollection.Database;
            }
        }
        public IBsonSerializer<TDocument> DocumentSerializer
        {
            get
            {
                VerifyReadPermission();
                return mongoCollection.DocumentSerializer;
            }
        }
        public IMongoIndexManager<TDocument> Indexes
        {
            get
            {
                VerifyReadPermission();
                return mongoCollection.Indexes;
            }
        }
        public IMongoSearchIndexManager SearchIndexes
        {
            get
            {
                VerifyReadPermission();
                return mongoCollection.SearchIndexes;
            }
        }
        public MongoCollectionSettings Settings
        {
            get
            {
                VerifyReadPermission();
                return mongoCollection.Settings;
            }
        }

        // Methods.
        public IAsyncCursor<TResult> Aggregate<TResult>(
            PipelineDefinition<TDocument, TResult> pipeline,
            AggregateOptions? options = null,
            CancellationToken cancellationToken = new()) =>
            Aggregate(null, pipeline, options, cancellationToken);

        public IAsyncCursor<TResult> Aggregate<TResult>(
            IClientSessionHandle? session,
            PipelineDefinition<TDocument, TResult> pipeline,
            AggregateOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyReadPermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.Aggregate(effectiveSession, pipeline, options, cancellationToken)
                : mongoCollection.Aggregate(pipeline, options, cancellationToken);
        }

        public Task<IAsyncCursor<TResult>> AggregateAsync<TResult>(
            PipelineDefinition<TDocument, TResult> pipeline,
            AggregateOptions? options = null,
            CancellationToken cancellationToken = new()) =>
            AggregateAsync(null, pipeline, options, cancellationToken);

        public Task<IAsyncCursor<TResult>> AggregateAsync<TResult>(
            IClientSessionHandle? session,
            PipelineDefinition<TDocument, TResult> pipeline,
            AggregateOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyReadPermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.AggregateAsync(effectiveSession, pipeline, options, cancellationToken)
                : mongoCollection.AggregateAsync(pipeline, options, cancellationToken);
        }

        public void AggregateToCollection<TResult>(
            PipelineDefinition<TDocument, TResult> pipeline,
            AggregateOptions? options = null,
            CancellationToken cancellationToken = new()) =>
            AggregateToCollection(null, pipeline, options, cancellationToken);

        public void AggregateToCollection<TResult>(
            IClientSessionHandle? session,
            PipelineDefinition<TDocument, TResult> pipeline,
            AggregateOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyWritePermission();
            if ((session ?? TryGetAmbientSession()) is { } effectiveSession)
                mongoCollection.AggregateToCollection(effectiveSession, pipeline, options, cancellationToken);
            else
                mongoCollection.AggregateToCollection(pipeline, options, cancellationToken);
        }

        public Task AggregateToCollectionAsync<TResult>(
            PipelineDefinition<TDocument, TResult> pipeline,
            AggregateOptions? options = null,
            CancellationToken cancellationToken = new()) =>
            AggregateToCollectionAsync(null, pipeline, options, cancellationToken);

        public Task AggregateToCollectionAsync<TResult>(
            IClientSessionHandle? session,
            PipelineDefinition<TDocument, TResult> pipeline,
            AggregateOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyWritePermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.AggregateToCollectionAsync(effectiveSession, pipeline, options, cancellationToken)
                : mongoCollection.AggregateToCollectionAsync(pipeline, options, cancellationToken);
        }

        public BulkWriteResult<TDocument> BulkWrite(
            IEnumerable<WriteModel<TDocument>> requests,
            BulkWriteOptions? options = null,
            CancellationToken cancellationToken = new()) =>
            BulkWrite(null, requests, options, cancellationToken);

        public BulkWriteResult<TDocument> BulkWrite(
            IClientSessionHandle? session,
            IEnumerable<WriteModel<TDocument>> requests,
            BulkWriteOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyWritePermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.BulkWrite(effectiveSession, requests, options, cancellationToken)
                : mongoCollection.BulkWrite(requests, options, cancellationToken);
        }

        public Task<BulkWriteResult<TDocument>> BulkWriteAsync(
            IEnumerable<WriteModel<TDocument>> requests,
            BulkWriteOptions? options = null,
            CancellationToken cancellationToken = new()) =>
            BulkWriteAsync(null, requests, options, cancellationToken);

        public Task<BulkWriteResult<TDocument>> BulkWriteAsync(
            IClientSessionHandle? session,
            IEnumerable<WriteModel<TDocument>> requests,
            BulkWriteOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyWritePermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.BulkWriteAsync(effectiveSession, requests, options, cancellationToken)
                : mongoCollection.BulkWriteAsync(requests, options, cancellationToken);
        }

        [Obsolete("Use CountDocuments or EstimatedDocumentCount instead.")]
        public long Count(
            FilterDefinition<TDocument> filter,
            CountOptions? options = null,
            CancellationToken cancellationToken = new()) =>
            Count(null, filter, options, cancellationToken);

        [Obsolete("Use CountDocuments or EstimatedDocumentCount instead.")]
        public long Count(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            CountOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyReadPermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.Count(effectiveSession, filter, options, cancellationToken)
                : mongoCollection.Count(filter, options, cancellationToken);
        }

        [Obsolete("Use CountDocuments or EstimatedDocumentCount instead.")]
        public Task<long> CountAsync(
            FilterDefinition<TDocument> filter,
            CountOptions? options = null,
            CancellationToken cancellationToken = new()) =>
            CountAsync(null, filter, options, cancellationToken);

        [Obsolete("Use CountDocuments or EstimatedDocumentCount instead.")]
        public Task<long> CountAsync(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            CountOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyReadPermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.CountAsync(effectiveSession, filter, options, cancellationToken)
                : mongoCollection.CountAsync(filter, options, cancellationToken);
        }

        public long CountDocuments(
            FilterDefinition<TDocument> filter,
            CountOptions? options = null,
            CancellationToken cancellationToken = new()) =>
            CountDocuments(null, filter, options, cancellationToken);

        public long CountDocuments(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            CountOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyReadPermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.CountDocuments(effectiveSession, filter, options, cancellationToken)
                : mongoCollection.CountDocuments(filter, options, cancellationToken);
        }

        public Task<long> CountDocumentsAsync(
            FilterDefinition<TDocument> filter,
            CountOptions? options = null,
            CancellationToken cancellationToken = new()) =>
            CountDocumentsAsync(null, filter, options, cancellationToken);

        public Task<long> CountDocumentsAsync(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            CountOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyReadPermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.CountDocumentsAsync(effectiveSession, filter, options, cancellationToken)
                : mongoCollection.CountDocumentsAsync(filter, options, cancellationToken);
        }

        public DeleteResult DeleteMany(
            FilterDefinition<TDocument> filter,
            CancellationToken cancellationToken = new()) =>
            DeleteMany(null, filter, null, cancellationToken);

        public DeleteResult DeleteMany(
            FilterDefinition<TDocument> filter,
            DeleteOptions options,
            CancellationToken cancellationToken = new()) =>
            DeleteMany(null, filter, options, cancellationToken);

        public DeleteResult DeleteMany(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            DeleteOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyWritePermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.DeleteMany(effectiveSession, filter, options, cancellationToken)
                : mongoCollection.DeleteMany(filter, options, cancellationToken);
        }

        public Task<DeleteResult> DeleteManyAsync(
            FilterDefinition<TDocument> filter,
            CancellationToken cancellationToken = new()) =>
            DeleteManyAsync(null, filter, null, cancellationToken);

        public Task<DeleteResult> DeleteManyAsync(
            FilterDefinition<TDocument> filter,
            DeleteOptions options,
            CancellationToken cancellationToken = new()) =>
            DeleteManyAsync(null, filter, options, cancellationToken);

        public Task<DeleteResult> DeleteManyAsync(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            DeleteOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyWritePermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.DeleteManyAsync(effectiveSession, filter, options, cancellationToken)
                : mongoCollection.DeleteManyAsync(filter, options, cancellationToken);
        }

        public DeleteResult DeleteOne(
            FilterDefinition<TDocument> filter,
            CancellationToken cancellationToken = new()) =>
            DeleteOne(null, filter, null, cancellationToken);

        public DeleteResult DeleteOne(
            FilterDefinition<TDocument> filter,
            DeleteOptions options,
            CancellationToken cancellationToken = new()) =>
            DeleteOne(null, filter, options, cancellationToken);

        public DeleteResult DeleteOne(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            DeleteOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyWritePermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.DeleteOne(effectiveSession, filter, options, cancellationToken)
                : mongoCollection.DeleteOne(filter, options, cancellationToken);
        }

        public Task<DeleteResult> DeleteOneAsync(
            FilterDefinition<TDocument> filter,
            CancellationToken cancellationToken = new()) =>
            DeleteOneAsync(null, filter, null, cancellationToken);

        public Task<DeleteResult> DeleteOneAsync(
            FilterDefinition<TDocument> filter,
            DeleteOptions options,
            CancellationToken cancellationToken = new()) =>
            DeleteOneAsync(null, filter, options, cancellationToken);

        public Task<DeleteResult> DeleteOneAsync(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            DeleteOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyWritePermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.DeleteOneAsync(effectiveSession, filter, options, cancellationToken)
                : mongoCollection.DeleteOneAsync(filter, options, cancellationToken);
        }

        public IAsyncCursor<TField> Distinct<TField>(
            FieldDefinition<TDocument, TField> field,
            FilterDefinition<TDocument> filter,
            DistinctOptions? options = null,
            CancellationToken cancellationToken = new()) =>
            Distinct(null, field, filter, options, cancellationToken);

        public IAsyncCursor<TField> Distinct<TField>(
            IClientSessionHandle? session,
            FieldDefinition<TDocument, TField> field,
            FilterDefinition<TDocument> filter,
            DistinctOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyReadPermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.Distinct(effectiveSession, field, filter, options, cancellationToken)
                : mongoCollection.Distinct(field, filter, options, cancellationToken);
        }

        public Task<IAsyncCursor<TField>> DistinctAsync<TField>(
            FieldDefinition<TDocument, TField> field,
            FilterDefinition<TDocument> filter,
            DistinctOptions? options = null,
            CancellationToken cancellationToken = new()) =>
            DistinctAsync(null, field, filter, options, cancellationToken);

        public Task<IAsyncCursor<TField>> DistinctAsync<TField>(
            IClientSessionHandle? session,
            FieldDefinition<TDocument, TField> field,
            FilterDefinition<TDocument> filter,
            DistinctOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyReadPermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.DistinctAsync(effectiveSession, field, filter, options, cancellationToken)
                : mongoCollection.DistinctAsync(field, filter, options, cancellationToken);
        }

        public IAsyncCursor<TItem> DistinctMany<TItem>(
            FieldDefinition<TDocument, IEnumerable<TItem>> field,
            FilterDefinition<TDocument> filter,
            DistinctOptions? options = null,
            CancellationToken cancellationToken = new()) =>
            DistinctMany(null, field, filter, options, cancellationToken);

        public IAsyncCursor<TItem> DistinctMany<TItem>(
            IClientSessionHandle? session,
            FieldDefinition<TDocument, IEnumerable<TItem>> field,
            FilterDefinition<TDocument> filter,
            DistinctOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyReadPermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.DistinctMany(effectiveSession, field, filter, options, cancellationToken)
                : mongoCollection.DistinctMany(field, filter, options, cancellationToken);
        }

        public Task<IAsyncCursor<TItem>> DistinctManyAsync<TItem>(
            FieldDefinition<TDocument, IEnumerable<TItem>> field,
            FilterDefinition<TDocument> filter,
            DistinctOptions? options = null,
            CancellationToken cancellationToken = new()) =>
            DistinctManyAsync(null, field, filter, options, cancellationToken);

        public Task<IAsyncCursor<TItem>> DistinctManyAsync<TItem>(
            IClientSessionHandle? session,
            FieldDefinition<TDocument, IEnumerable<TItem>> field,
            FilterDefinition<TDocument> filter,
            DistinctOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyReadPermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.DistinctManyAsync(effectiveSession, field, filter, options, cancellationToken)
                : mongoCollection.DistinctManyAsync(field, filter, options, cancellationToken);
        }

        public long EstimatedDocumentCount(
            EstimatedDocumentCountOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyReadPermission();
            return mongoCollection.EstimatedDocumentCount(options, cancellationToken);
        }

        public Task<long> EstimatedDocumentCountAsync(
            EstimatedDocumentCountOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyReadPermission();
            return mongoCollection.EstimatedDocumentCountAsync(options, cancellationToken);
        }

        public IAsyncCursor<TProjection> FindSync<TProjection>(
            FilterDefinition<TDocument> filter,
            FindOptions<TDocument, TProjection>? options = null,
            CancellationToken cancellationToken = new()) =>
            FindSync(null, filter, options, cancellationToken);

        public IAsyncCursor<TProjection> FindSync<TProjection>(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            FindOptions<TDocument, TProjection>? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyReadPermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.FindSync(effectiveSession, filter, options, cancellationToken)
                : mongoCollection.FindSync(filter, options, cancellationToken);
        }

        public Task<IAsyncCursor<TProjection>> FindAsync<TProjection>(
            FilterDefinition<TDocument> filter,
            FindOptions<TDocument, TProjection>? options = null,
            CancellationToken cancellationToken = new()) =>
            FindAsync(null, filter, options, cancellationToken);

        public Task<IAsyncCursor<TProjection>> FindAsync<TProjection>(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            FindOptions<TDocument, TProjection>? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyReadPermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.FindAsync(effectiveSession, filter, options, cancellationToken)
                : mongoCollection.FindAsync(filter, options, cancellationToken);
        }

        public TProjection FindOneAndDelete<TProjection>(
            FilterDefinition<TDocument> filter,
            FindOneAndDeleteOptions<TDocument, TProjection>? options = null,
            CancellationToken cancellationToken = new()) =>
            FindOneAndDelete(null, filter, options, cancellationToken);

        public TProjection FindOneAndDelete<TProjection>(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            FindOneAndDeleteOptions<TDocument, TProjection>? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyWritePermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.FindOneAndDelete(effectiveSession, filter, options, cancellationToken)
                : mongoCollection.FindOneAndDelete(filter, options, cancellationToken);
        }

        public Task<TProjection> FindOneAndDeleteAsync<TProjection>(
            FilterDefinition<TDocument> filter,
            FindOneAndDeleteOptions<TDocument, TProjection>? options = null,
            CancellationToken cancellationToken = new()) =>
            FindOneAndDeleteAsync(null, filter, options, cancellationToken);

        public Task<TProjection> FindOneAndDeleteAsync<TProjection>(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            FindOneAndDeleteOptions<TDocument, TProjection>? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyWritePermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.FindOneAndDeleteAsync(effectiveSession, filter, options, cancellationToken)
                : mongoCollection.FindOneAndDeleteAsync(filter, options, cancellationToken);
        }

        public TProjection FindOneAndReplace<TProjection>(
            FilterDefinition<TDocument> filter,
            TDocument replacement,
            FindOneAndReplaceOptions<TDocument, TProjection>? options = null,
            CancellationToken cancellationToken = new()) =>
            FindOneAndReplace(null, filter, replacement, options, cancellationToken);

        public TProjection FindOneAndReplace<TProjection>(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            TDocument replacement,
            FindOneAndReplaceOptions<TDocument, TProjection>? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyWritePermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.FindOneAndReplace(effectiveSession, filter, replacement, options, cancellationToken)
                : mongoCollection.FindOneAndReplace(filter, replacement, options, cancellationToken);
        }

        public Task<TProjection> FindOneAndReplaceAsync<TProjection>(
            FilterDefinition<TDocument> filter,
            TDocument replacement,
            FindOneAndReplaceOptions<TDocument, TProjection>? options = null,
            CancellationToken cancellationToken = new()) =>
            FindOneAndReplaceAsync(null, filter, replacement, options, cancellationToken);

        public Task<TProjection> FindOneAndReplaceAsync<TProjection>(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            TDocument replacement,
            FindOneAndReplaceOptions<TDocument, TProjection>? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyWritePermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.FindOneAndReplaceAsync(effectiveSession, filter, replacement, options, cancellationToken)
                : mongoCollection.FindOneAndReplaceAsync(filter, replacement, options, cancellationToken);
        }

        public TProjection FindOneAndUpdate<TProjection>(
            FilterDefinition<TDocument> filter,
            UpdateDefinition<TDocument> update,
            FindOneAndUpdateOptions<TDocument, TProjection>? options = null,
            CancellationToken cancellationToken = new()) =>
            FindOneAndUpdate(null, filter, update, options, cancellationToken);

        public TProjection FindOneAndUpdate<TProjection>(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            UpdateDefinition<TDocument> update,
            FindOneAndUpdateOptions<TDocument, TProjection>? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyWritePermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.FindOneAndUpdate(effectiveSession, filter, update, options, cancellationToken)
                : mongoCollection.FindOneAndUpdate(filter, update, options, cancellationToken);
        }

        public Task<TProjection> FindOneAndUpdateAsync<TProjection>(
            FilterDefinition<TDocument> filter,
            UpdateDefinition<TDocument> update,
            FindOneAndUpdateOptions<TDocument, TProjection>? options = null,
            CancellationToken cancellationToken = new()) =>
            FindOneAndUpdateAsync(null, filter, update, options, cancellationToken);

        public Task<TProjection> FindOneAndUpdateAsync<TProjection>(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            UpdateDefinition<TDocument> update,
            FindOneAndUpdateOptions<TDocument, TProjection>? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyWritePermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.FindOneAndUpdateAsync(effectiveSession, filter, update, options, cancellationToken)
                : mongoCollection.FindOneAndUpdateAsync(filter, update, options, cancellationToken);
        }

        public void InsertOne(
            TDocument document,
            InsertOneOptions? options = null,
            CancellationToken cancellationToken = new()) =>
            InsertOne(null, document, options, cancellationToken);

        public void InsertOne(
            IClientSessionHandle? session,
            TDocument document,
            InsertOneOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyWritePermission();
            if ((session ?? TryGetAmbientSession()) is { } effectiveSession)
                mongoCollection.InsertOne(effectiveSession, document, options, cancellationToken);
            else
                mongoCollection.InsertOne(document, options, cancellationToken);
        }

        [Obsolete("Use the new overload of InsertOneAsync with an InsertOneOptions parameter instead.")]
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        public Task InsertOneAsync(
            TDocument document,
            CancellationToken _cancellationToken) =>
            InsertOneAsync(null, document, null, _cancellationToken);

        public Task InsertOneAsync(
            TDocument document,
            InsertOneOptions? options = null,
            CancellationToken cancellationToken = new()) =>
            InsertOneAsync(null, document, options, cancellationToken);

        public Task InsertOneAsync(
            IClientSessionHandle? session,
            TDocument document,
            InsertOneOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyWritePermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.InsertOneAsync(effectiveSession, document, options, cancellationToken)
                : mongoCollection.InsertOneAsync(document, options, cancellationToken);
        }

        public void InsertMany(
            IEnumerable<TDocument> documents,
            InsertManyOptions? options = null,
            CancellationToken cancellationToken = new()) =>
            InsertMany(null, documents, options, cancellationToken);

        public void InsertMany(
            IClientSessionHandle? session,
            IEnumerable<TDocument> documents,
            InsertManyOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyWritePermission();
            if ((session ?? TryGetAmbientSession()) is { } effectiveSession)
                mongoCollection.InsertMany(effectiveSession, documents, options, cancellationToken);
            else
                mongoCollection.InsertMany(documents, options, cancellationToken);
        }

        public Task InsertManyAsync(
            IEnumerable<TDocument> documents,
            InsertManyOptions? options = null,
            CancellationToken cancellationToken = new()) =>
            InsertManyAsync(null, documents, options, cancellationToken);

        public Task InsertManyAsync(
            IClientSessionHandle? session,
            IEnumerable<TDocument> documents,
            InsertManyOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyWritePermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.InsertManyAsync(effectiveSession, documents, options, cancellationToken)
                : mongoCollection.InsertManyAsync(documents, options, cancellationToken);
        }

        [Obsolete("Use Aggregation pipeline instead.")]
        public IAsyncCursor<TResult> MapReduce<TResult>(
            BsonJavaScript map,
            BsonJavaScript reduce,
            MapReduceOptions<TDocument, TResult>? options = null,
            CancellationToken cancellationToken = new()) =>
            MapReduce(null, map, reduce, options, cancellationToken);

        [Obsolete("Use Aggregation pipeline instead.")]
        public IAsyncCursor<TResult> MapReduce<TResult>(
            IClientSessionHandle? session,
            BsonJavaScript map,
            BsonJavaScript reduce,
            MapReduceOptions<TDocument, TResult>? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyReadPermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.MapReduce(effectiveSession, map, reduce, options, cancellationToken)
                : mongoCollection.MapReduce(map, reduce, options, cancellationToken);
        }

        [Obsolete("Use Aggregation pipeline instead.")]
        public Task<IAsyncCursor<TResult>> MapReduceAsync<TResult>(
            BsonJavaScript map,
            BsonJavaScript reduce,
            MapReduceOptions<TDocument, TResult>? options = null,
            CancellationToken cancellationToken = new()) =>
            MapReduceAsync(null, map, reduce, options, cancellationToken);

        [Obsolete("Use Aggregation pipeline instead.")]
        public Task<IAsyncCursor<TResult>> MapReduceAsync<TResult>(
            IClientSessionHandle? session,
            BsonJavaScript map,
            BsonJavaScript reduce,
            MapReduceOptions<TDocument, TResult>? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyReadPermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.MapReduceAsync(effectiveSession, map, reduce, options, cancellationToken)
                : mongoCollection.MapReduceAsync(map, reduce, options, cancellationToken);
        }

        public IFilteredMongoCollection<TDerivedDocument> OfType<TDerivedDocument>()
            where TDerivedDocument : TDocument
        {
            VerifyReadPermission();
            return mongoCollection.OfType<TDerivedDocument>();
        }

        public ReplaceOneResult ReplaceOne(
            FilterDefinition<TDocument> filter,
            TDocument replacement,
            ReplaceOptions? options = null,
            CancellationToken cancellationToken = new()) =>
            ReplaceOne(null, filter, replacement, options, cancellationToken);

        [Obsolete("Use the overload that takes a ReplaceOptions instead of an UpdateOptions.")]
        public ReplaceOneResult ReplaceOne(
            FilterDefinition<TDocument> filter,
            TDocument replacement,
            UpdateOptions options,
            CancellationToken cancellationToken = new()) =>
            ReplaceOne(null, filter, replacement, options, cancellationToken);

        public ReplaceOneResult ReplaceOne(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            TDocument replacement,
            ReplaceOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyWritePermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.ReplaceOne(effectiveSession, filter, replacement, options, cancellationToken)
                : mongoCollection.ReplaceOne(filter, replacement, options, cancellationToken);
        }

        [Obsolete("Use the overload that takes a ReplaceOptions instead of an UpdateOptions.")]
        public ReplaceOneResult ReplaceOne(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            TDocument replacement,
            UpdateOptions options,
            CancellationToken cancellationToken = new())
        {
            VerifyWritePermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.ReplaceOne(effectiveSession, filter, replacement, options, cancellationToken)
                : mongoCollection.ReplaceOne(filter, replacement, options, cancellationToken);
        }

        public Task<ReplaceOneResult> ReplaceOneAsync(
            FilterDefinition<TDocument> filter,
            TDocument replacement,
            ReplaceOptions? options = null,
            CancellationToken cancellationToken = new()) =>
            ReplaceOneAsync(null, filter, replacement, options, cancellationToken);

        [Obsolete("Use the overload that takes a ReplaceOptions instead of an UpdateOptions.")]
        public Task<ReplaceOneResult> ReplaceOneAsync(
            FilterDefinition<TDocument> filter,
            TDocument replacement,
            UpdateOptions options,
            CancellationToken cancellationToken = new()) =>
            ReplaceOneAsync(null, filter, replacement, options, cancellationToken);

        public Task<ReplaceOneResult> ReplaceOneAsync(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            TDocument replacement,
            ReplaceOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyWritePermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.ReplaceOneAsync(effectiveSession, filter, replacement, options, cancellationToken)
                : mongoCollection.ReplaceOneAsync(filter, replacement, options, cancellationToken);
        }

        [Obsolete("Use the overload that takes a ReplaceOptions instead of an UpdateOptions.")]
        public Task<ReplaceOneResult> ReplaceOneAsync(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            TDocument replacement,
            UpdateOptions options,
            CancellationToken cancellationToken = new())
        {
            VerifyWritePermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.ReplaceOneAsync(effectiveSession, filter, replacement, options, cancellationToken)
                : mongoCollection.ReplaceOneAsync(filter, replacement, options, cancellationToken);
        }

        public UpdateResult UpdateMany(
            FilterDefinition<TDocument> filter,
            UpdateDefinition<TDocument> update,
            UpdateOptions? options = null,
            CancellationToken cancellationToken = new()) =>
            UpdateMany(null, filter, update, options, cancellationToken);

        public UpdateResult UpdateMany(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            UpdateDefinition<TDocument> update,
            UpdateOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyWritePermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.UpdateMany(effectiveSession, filter, update, options, cancellationToken)
                : mongoCollection.UpdateMany(filter, update, options, cancellationToken);
        }

        public Task<UpdateResult> UpdateManyAsync(
            FilterDefinition<TDocument> filter,
            UpdateDefinition<TDocument> update,
            UpdateOptions? options = null,
            CancellationToken cancellationToken = new()) =>
            UpdateManyAsync(null, filter, update, options, cancellationToken);

        public Task<UpdateResult> UpdateManyAsync(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            UpdateDefinition<TDocument> update,
            UpdateOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyWritePermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.UpdateManyAsync(effectiveSession, filter, update, options, cancellationToken)
                : mongoCollection.UpdateManyAsync(filter, update, options, cancellationToken);
        }

        public UpdateResult UpdateOne(
            FilterDefinition<TDocument> filter,
            UpdateDefinition<TDocument> update,
            UpdateOptions? options = null,
            CancellationToken cancellationToken = new()) =>
            UpdateOne(null, filter, update, options, cancellationToken);

        public UpdateResult UpdateOne(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            UpdateDefinition<TDocument> update,
            UpdateOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyWritePermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.UpdateOne(effectiveSession, filter, update, options, cancellationToken)
                : mongoCollection.UpdateOne(filter, update, options, cancellationToken);
        }

        public Task<UpdateResult> UpdateOneAsync(
            FilterDefinition<TDocument> filter,
            UpdateDefinition<TDocument> update,
            UpdateOptions? options = null,
            CancellationToken cancellationToken = new()) =>
            UpdateOneAsync(null, filter, update, options, cancellationToken);

        public Task<UpdateResult> UpdateOneAsync(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            UpdateDefinition<TDocument> update,
            UpdateOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyWritePermission();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.UpdateOneAsync(effectiveSession, filter, update, options, cancellationToken)
                : mongoCollection.UpdateOneAsync(filter, update, options, cancellationToken);
        }

        public IChangeStreamCursor<TResult> Watch<TResult>(
            PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
            ChangeStreamOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyReadPermission();
            return mongoCollection.Watch(pipeline, options, cancellationToken);
        }

        public IChangeStreamCursor<TResult> Watch<TResult>(
            IClientSessionHandle session,
            PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
            ChangeStreamOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyReadPermission();
            return mongoCollection.Watch(session, pipeline, options, cancellationToken);
        }

        public Task<IChangeStreamCursor<TResult>> WatchAsync<TResult>(
            PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
            ChangeStreamOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyReadPermission();
            return mongoCollection.WatchAsync(pipeline, options, cancellationToken);
        }

        public Task<IChangeStreamCursor<TResult>> WatchAsync<TResult>(
            IClientSessionHandle session,
            PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
            ChangeStreamOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            VerifyReadPermission();
            return mongoCollection.WatchAsync(session, pipeline, options, cancellationToken);
        }

        public IMongoCollection<TDocument> WithReadConcern(ReadConcern readConcern)
        {
            var collection = mongoCollection.WithReadConcern(readConcern);
            return new LimitedAccessMongoCollection<TDocument>(dbContextEngine, collection, isReadOnly);
        }

        public IMongoCollection<TDocument> WithReadPreference(ReadPreference readPreference)
        {
            var collection = mongoCollection.WithReadPreference(readPreference);
            return new LimitedAccessMongoCollection<TDocument>(dbContextEngine, collection, isReadOnly);
        }

        public IMongoCollection<TDocument> WithWriteConcern(WriteConcern writeConcern)
        {
            var collection = mongoCollection.WithWriteConcern(writeConcern);
            return new LimitedAccessMongoCollection<TDocument>(dbContextEngine, collection, isReadOnly);
        }

        // Helpers.
        private IClientSessionHandle? TryGetAmbientSession() =>
            DbSessionHandler.TryGetCurrentSession(dbContextEngine);

        private void VerifyReadPermission()
        {
            if (dbContextEngine.IsExclusiveReadEnabled &&
                !ExclusiveAccessHandler.IsExclusiveAccessAllowed(dbContextEngine.ExecutionContext))
                throw new UnauthorizedAccessException("Read access is not allowed");
        }

        private void VerifyWritePermission()
        {
            if (isReadOnly)
                throw new UnauthorizedAccessException("Collection is read only");

            if (dbContextEngine.IsExclusiveWriteEnabled &&
                !ExclusiveAccessHandler.IsExclusiveAccessAllowed(dbContextEngine.ExecutionContext))
                throw new UnauthorizedAccessException("Write access is not allowed");
        }
    }
}
