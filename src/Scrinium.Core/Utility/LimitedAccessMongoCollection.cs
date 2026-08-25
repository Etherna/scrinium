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
using Etherna.MongoDB.Driver.Search;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.Scrinium.Core.Utility
{
    /* Each operation is implemented once by its session overload, that relaxes the session
     * parameter to nullable: an explicit session is used verbatim, a null one resolves to
     * the ambient session of the engine when active (e.g. a transaction started by
     * ExecuteInTransactionAsync), and without any session the operation runs session-less.
     * The session-less overloads forward with a null session. Change stream watches and
     * estimated document counts stay session-less: they can't run in transactions.
     *
     * Every member enters an operation scope verifying its access permission, and counting
     * the operation in flight on the engine until it completes — the span of the forwarded
     * call, closed when its task completes: the exclusive access window drains the counted
     * operations before starting its work. The operations admitted by an exclusive access
     * allowance don't stay counted, being the ones meant to work during the window, and
     * the cursors already handed out iterate past their originating call.
     *
     * A read-only collection denies any write operation, index management included: the
     * write permission verification throws UnauthorizedAccessException. Reads keep
     * working, exclusive access permitting.
     *
     * An aggregation whose rendered pipeline ends in a $out or $merge stage, and a map
     * reduce with output options other than inline, make the server write the results
     * into a named collection, possibly in another database: they verify the write
     * permission, detected with the same signal the driver uses to execute them as write
     * operations, while any other aggregation or map reduce verifies the read one.
     *
     * A dry run flow (marked by an ambient DryRunHandler) simulates writes: each write
     * operation executes its client side work (filter and update rendering, document
     * serialization) exactly as the real operation would before sending the command, and
     * returns an acknowledged result without touching the server. Simulated single document
     * write results report the matched document as existing, multi document ones report
     * zero matches. Writes without a client side half (index management, aggregate to
     * collection, map reduce with an output collection) can't be simulated and throw.
     *
     * Members handing out other driver objects (Database, Indexes, SearchIndexes, OfType
     * and the With* combinators) return equally guarded wrappers enforcing the same
     * access limitations, so operations reached through them can't escape the guards.
     * The only deliberate exit is the raw driver client, reachable through
     * Database.Client like through IDbContextEngine.Client. */
    [SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix")]
    public class LimitedAccessMongoCollection<TDocument>(
        IDbContextEngine dbContextEngine,
        IMongoCollection<TDocument> mongoCollection,
        bool isReadOnly)
        : IMongoCollection<TDocument>
    {
        // Fields.
        private readonly InFlightOperationsCounter inFlightOperations =
            ((IInternalDbContextEngine)dbContextEngine).InFlightOperations;

        // Properties.
        public CollectionNamespace CollectionNamespace
        {
            get
            {
                using var _ = EnterReadOperation();
                return mongoCollection.CollectionNamespace;
            }
        }
        public IMongoDatabase Database
        {
            get
            {
                using var _ = EnterReadOperation();
                return new LimitedAccessMongoDatabase(dbContextEngine, mongoCollection.Database, isReadOnly);
            }
        }
        public IBsonSerializer<TDocument> DocumentSerializer
        {
            get
            {
                using var _ = EnterReadOperation();
                return mongoCollection.DocumentSerializer;
            }
        }
        public IMongoIndexManager<TDocument> Indexes
        {
            get
            {
                using var _ = EnterReadOperation();
                return new LimitedAccessMongoIndexManager<TDocument>(
                    mongoCollection.Indexes,
                    EnterReadOperation,
                    EnterIndexWriteOperation);
            }
        }
        public IMongoSearchIndexManager SearchIndexes
        {
            get
            {
                using var _ = EnterReadOperation();
                return new LimitedAccessMongoSearchIndexManager(
                    mongoCollection.SearchIndexes,
                    EnterReadOperation,
                    EnterIndexWriteOperation);
            }
        }
        public MongoCollectionSettings Settings
        {
            get
            {
                using var _ = EnterReadOperation();
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
            using var _ = EnterAggregateOperation(pipeline);
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.Aggregate(effectiveSession, pipeline, options, cancellationToken)
                : mongoCollection.Aggregate(pipeline, options, cancellationToken);
        }

        public Task<IAsyncCursor<TResult>> AggregateAsync<TResult>(
            PipelineDefinition<TDocument, TResult> pipeline,
            AggregateOptions? options = null,
            CancellationToken cancellationToken = new()) =>
            AggregateAsync(null, pipeline, options, cancellationToken);

        public async Task<IAsyncCursor<TResult>> AggregateAsync<TResult>(
            IClientSessionHandle? session,
            PipelineDefinition<TDocument, TResult> pipeline,
            AggregateOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            using var _ = EnterAggregateOperation(pipeline);
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? await mongoCollection.AggregateAsync(effectiveSession, pipeline, options, cancellationToken).ConfigureAwait(false)
                : await mongoCollection.AggregateAsync(pipeline, options, cancellationToken).ConfigureAwait(false);
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
            using var _ = EnterWriteOperation("Aggregate to collection");
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

        public async Task AggregateToCollectionAsync<TResult>(
            IClientSessionHandle? session,
            PipelineDefinition<TDocument, TResult> pipeline,
            AggregateOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            using var _ = EnterWriteOperation("Aggregate to collection");
            if ((session ?? TryGetAmbientSession()) is { } effectiveSession)
                await mongoCollection.AggregateToCollectionAsync(effectiveSession, pipeline, options, cancellationToken).ConfigureAwait(false);
            else
                await mongoCollection.AggregateToCollectionAsync(pipeline, options, cancellationToken).ConfigureAwait(false);
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
            using var _ = EnterWriteOperation();
            if (IsDryRun())
                return SimulateBulkWrite(requests);
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.BulkWrite(effectiveSession, requests, options, cancellationToken)
                : mongoCollection.BulkWrite(requests, options, cancellationToken);
        }

        public Task<BulkWriteResult<TDocument>> BulkWriteAsync(
            IEnumerable<WriteModel<TDocument>> requests,
            BulkWriteOptions? options = null,
            CancellationToken cancellationToken = new()) =>
            BulkWriteAsync(null, requests, options, cancellationToken);

        public async Task<BulkWriteResult<TDocument>> BulkWriteAsync(
            IClientSessionHandle? session,
            IEnumerable<WriteModel<TDocument>> requests,
            BulkWriteOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            using var _ = EnterWriteOperation();
            if (IsDryRun())
                return SimulateBulkWrite(requests);
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? await mongoCollection.BulkWriteAsync(effectiveSession, requests, options, cancellationToken).ConfigureAwait(false)
                : await mongoCollection.BulkWriteAsync(requests, options, cancellationToken).ConfigureAwait(false);
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
            using var _ = EnterReadOperation();
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
        public async Task<long> CountAsync(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            CountOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            using var _ = EnterReadOperation();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? await mongoCollection.CountAsync(effectiveSession, filter, options, cancellationToken).ConfigureAwait(false)
                : await mongoCollection.CountAsync(filter, options, cancellationToken).ConfigureAwait(false);
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
            using var _ = EnterReadOperation();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.CountDocuments(effectiveSession, filter, options, cancellationToken)
                : mongoCollection.CountDocuments(filter, options, cancellationToken);
        }

        public Task<long> CountDocumentsAsync(
            FilterDefinition<TDocument> filter,
            CountOptions? options = null,
            CancellationToken cancellationToken = new()) =>
            CountDocumentsAsync(null, filter, options, cancellationToken);

        public async Task<long> CountDocumentsAsync(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            CountOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            using var _ = EnterReadOperation();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? await mongoCollection.CountDocumentsAsync(effectiveSession, filter, options, cancellationToken).ConfigureAwait(false)
                : await mongoCollection.CountDocumentsAsync(filter, options, cancellationToken).ConfigureAwait(false);
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
            using var _ = EnterWriteOperation();
            if (IsDryRun())
            {
                SimulateWrite(filter);
                return new DeleteResult.Acknowledged(0);
            }
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

        public async Task<DeleteResult> DeleteManyAsync(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            DeleteOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            using var _ = EnterWriteOperation();
            if (IsDryRun())
            {
                SimulateWrite(filter);
                return new DeleteResult.Acknowledged(0);
            }
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? await mongoCollection.DeleteManyAsync(effectiveSession, filter, options, cancellationToken).ConfigureAwait(false)
                : await mongoCollection.DeleteManyAsync(filter, options, cancellationToken).ConfigureAwait(false);
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
            using var _ = EnterWriteOperation();
            if (IsDryRun())
            {
                SimulateWrite(filter);
                return new DeleteResult.Acknowledged(1);
            }
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

        public async Task<DeleteResult> DeleteOneAsync(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            DeleteOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            using var _ = EnterWriteOperation();
            if (IsDryRun())
            {
                SimulateWrite(filter);
                return new DeleteResult.Acknowledged(1);
            }
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? await mongoCollection.DeleteOneAsync(effectiveSession, filter, options, cancellationToken).ConfigureAwait(false)
                : await mongoCollection.DeleteOneAsync(filter, options, cancellationToken).ConfigureAwait(false);
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
            using var _ = EnterReadOperation();
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

        public async Task<IAsyncCursor<TField>> DistinctAsync<TField>(
            IClientSessionHandle? session,
            FieldDefinition<TDocument, TField> field,
            FilterDefinition<TDocument> filter,
            DistinctOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            using var _ = EnterReadOperation();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? await mongoCollection.DistinctAsync(effectiveSession, field, filter, options, cancellationToken).ConfigureAwait(false)
                : await mongoCollection.DistinctAsync(field, filter, options, cancellationToken).ConfigureAwait(false);
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
            using var _ = EnterReadOperation();
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

        public async Task<IAsyncCursor<TItem>> DistinctManyAsync<TItem>(
            IClientSessionHandle? session,
            FieldDefinition<TDocument, IEnumerable<TItem>> field,
            FilterDefinition<TDocument> filter,
            DistinctOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            using var _ = EnterReadOperation();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? await mongoCollection.DistinctManyAsync(effectiveSession, field, filter, options, cancellationToken).ConfigureAwait(false)
                : await mongoCollection.DistinctManyAsync(field, filter, options, cancellationToken).ConfigureAwait(false);
        }

        public long EstimatedDocumentCount(
            EstimatedDocumentCountOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            using var _ = EnterReadOperation();
            return mongoCollection.EstimatedDocumentCount(options, cancellationToken);
        }

        public async Task<long> EstimatedDocumentCountAsync(
            EstimatedDocumentCountOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            using var _ = EnterReadOperation();
            return await mongoCollection.EstimatedDocumentCountAsync(options, cancellationToken).ConfigureAwait(false);
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
            using var _ = EnterReadOperation();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.FindSync(effectiveSession, filter, options, cancellationToken)
                : mongoCollection.FindSync(filter, options, cancellationToken);
        }

        public Task<IAsyncCursor<TProjection>> FindAsync<TProjection>(
            FilterDefinition<TDocument> filter,
            FindOptions<TDocument, TProjection>? options = null,
            CancellationToken cancellationToken = new()) =>
            FindAsync(null, filter, options, cancellationToken);

        public async Task<IAsyncCursor<TProjection>> FindAsync<TProjection>(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            FindOptions<TDocument, TProjection>? options = null,
            CancellationToken cancellationToken = new())
        {
            using var _ = EnterReadOperation();
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? await mongoCollection.FindAsync(effectiveSession, filter, options, cancellationToken).ConfigureAwait(false)
                : await mongoCollection.FindAsync(filter, options, cancellationToken).ConfigureAwait(false);
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
            using var _ = EnterWriteOperation();
            if (IsDryRun())
            {
                SimulateWrite(filter);
                return default!;
            }
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? mongoCollection.FindOneAndDelete(effectiveSession, filter, options, cancellationToken)
                : mongoCollection.FindOneAndDelete(filter, options, cancellationToken);
        }

        public Task<TProjection> FindOneAndDeleteAsync<TProjection>(
            FilterDefinition<TDocument> filter,
            FindOneAndDeleteOptions<TDocument, TProjection>? options = null,
            CancellationToken cancellationToken = new()) =>
            FindOneAndDeleteAsync(null, filter, options, cancellationToken);

        public async Task<TProjection> FindOneAndDeleteAsync<TProjection>(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            FindOneAndDeleteOptions<TDocument, TProjection>? options = null,
            CancellationToken cancellationToken = new())
        {
            using var _ = EnterWriteOperation();
            if (IsDryRun())
            {
                SimulateWrite(filter);
                return default!;
            }
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? await mongoCollection.FindOneAndDeleteAsync(effectiveSession, filter, options, cancellationToken).ConfigureAwait(false)
                : await mongoCollection.FindOneAndDeleteAsync(filter, options, cancellationToken).ConfigureAwait(false);
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
            using var _ = EnterWriteOperation();
            if (IsDryRun())
            {
                SimulateWrite(filter, documents: [replacement]);
                return default!;
            }
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

        public async Task<TProjection> FindOneAndReplaceAsync<TProjection>(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            TDocument replacement,
            FindOneAndReplaceOptions<TDocument, TProjection>? options = null,
            CancellationToken cancellationToken = new())
        {
            using var _ = EnterWriteOperation();
            if (IsDryRun())
            {
                SimulateWrite(filter, documents: [replacement]);
                return default!;
            }
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? await mongoCollection.FindOneAndReplaceAsync(effectiveSession, filter, replacement, options, cancellationToken).ConfigureAwait(false)
                : await mongoCollection.FindOneAndReplaceAsync(filter, replacement, options, cancellationToken).ConfigureAwait(false);
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
            using var _ = EnterWriteOperation();
            if (IsDryRun())
            {
                SimulateWrite(filter, update);
                return default!;
            }
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

        public async Task<TProjection> FindOneAndUpdateAsync<TProjection>(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            UpdateDefinition<TDocument> update,
            FindOneAndUpdateOptions<TDocument, TProjection>? options = null,
            CancellationToken cancellationToken = new())
        {
            using var _ = EnterWriteOperation();
            if (IsDryRun())
            {
                SimulateWrite(filter, update);
                return default!;
            }
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? await mongoCollection.FindOneAndUpdateAsync(effectiveSession, filter, update, options, cancellationToken).ConfigureAwait(false)
                : await mongoCollection.FindOneAndUpdateAsync(filter, update, options, cancellationToken).ConfigureAwait(false);
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
            using var _ = EnterWriteOperation();
            if (IsDryRun())
            {
                SimulateWrite(documents: [document]);
                return;
            }
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

        public async Task InsertOneAsync(
            IClientSessionHandle? session,
            TDocument document,
            InsertOneOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            using var _ = EnterWriteOperation();
            if (IsDryRun())
            {
                SimulateWrite(documents: [document]);
                return;
            }
            if ((session ?? TryGetAmbientSession()) is { } effectiveSession)
                await mongoCollection.InsertOneAsync(effectiveSession, document, options, cancellationToken).ConfigureAwait(false);
            else
                await mongoCollection.InsertOneAsync(document, options, cancellationToken).ConfigureAwait(false);
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
            using var _ = EnterWriteOperation();
            if (IsDryRun())
            {
                SimulateWrite(documents: documents);
                return;
            }
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

        public async Task InsertManyAsync(
            IClientSessionHandle? session,
            IEnumerable<TDocument> documents,
            InsertManyOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            using var _ = EnterWriteOperation();
            if (IsDryRun())
            {
                SimulateWrite(documents: documents);
                return;
            }
            if ((session ?? TryGetAmbientSession()) is { } effectiveSession)
                await mongoCollection.InsertManyAsync(effectiveSession, documents, options, cancellationToken).ConfigureAwait(false);
            else
                await mongoCollection.InsertManyAsync(documents, options, cancellationToken).ConfigureAwait(false);
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
            using var _ = (options?.OutputOptions ?? MapReduceOutputOptions.Inline) == MapReduceOutputOptions.Inline
                ? EnterReadOperation()
                : EnterWriteOperation("Map reduce with an output collection");
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
        public async Task<IAsyncCursor<TResult>> MapReduceAsync<TResult>(
            IClientSessionHandle? session,
            BsonJavaScript map,
            BsonJavaScript reduce,
            MapReduceOptions<TDocument, TResult>? options = null,
            CancellationToken cancellationToken = new())
        {
            using var _ = (options?.OutputOptions ?? MapReduceOutputOptions.Inline) == MapReduceOutputOptions.Inline
                ? EnterReadOperation()
                : EnterWriteOperation("Map reduce with an output collection");
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? await mongoCollection.MapReduceAsync(effectiveSession, map, reduce, options, cancellationToken).ConfigureAwait(false)
                : await mongoCollection.MapReduceAsync(map, reduce, options, cancellationToken).ConfigureAwait(false);
        }

        public IFilteredMongoCollection<TDerivedDocument> OfType<TDerivedDocument>()
            where TDerivedDocument : TDocument
        {
            using var _ = EnterReadOperation();
            return new LimitedAccessFilteredMongoCollection<TDerivedDocument>(
                dbContextEngine,
                mongoCollection.OfType<TDerivedDocument>(),
                isReadOnly);
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
            using var _ = EnterWriteOperation();
            if (IsDryRun())
            {
                SimulateWrite(filter, documents: [replacement]);
                return new ReplaceOneResult.Acknowledged(1, 1, null);
            }
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
            using var _ = EnterWriteOperation();
            if (IsDryRun())
            {
                SimulateWrite(filter, documents: [replacement]);
                return new ReplaceOneResult.Acknowledged(1, 1, null);
            }
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

        public async Task<ReplaceOneResult> ReplaceOneAsync(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            TDocument replacement,
            ReplaceOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            using var _ = EnterWriteOperation();
            if (IsDryRun())
            {
                SimulateWrite(filter, documents: [replacement]);
                return new ReplaceOneResult.Acknowledged(1, 1, null);
            }
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? await mongoCollection.ReplaceOneAsync(effectiveSession, filter, replacement, options, cancellationToken).ConfigureAwait(false)
                : await mongoCollection.ReplaceOneAsync(filter, replacement, options, cancellationToken).ConfigureAwait(false);
        }

        [Obsolete("Use the overload that takes a ReplaceOptions instead of an UpdateOptions.")]
        public async Task<ReplaceOneResult> ReplaceOneAsync(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            TDocument replacement,
            UpdateOptions options,
            CancellationToken cancellationToken = new())
        {
            using var _ = EnterWriteOperation();
            if (IsDryRun())
            {
                SimulateWrite(filter, documents: [replacement]);
                return new ReplaceOneResult.Acknowledged(1, 1, null);
            }
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? await mongoCollection.ReplaceOneAsync(effectiveSession, filter, replacement, options, cancellationToken).ConfigureAwait(false)
                : await mongoCollection.ReplaceOneAsync(filter, replacement, options, cancellationToken).ConfigureAwait(false);
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
            using var _ = EnterWriteOperation();
            if (IsDryRun())
            {
                SimulateWrite(filter, update);
                return new UpdateResult.Acknowledged(0, 0, null);
            }
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

        public async Task<UpdateResult> UpdateManyAsync(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            UpdateDefinition<TDocument> update,
            UpdateOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            using var _ = EnterWriteOperation();
            if (IsDryRun())
            {
                SimulateWrite(filter, update);
                return new UpdateResult.Acknowledged(0, 0, null);
            }
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? await mongoCollection.UpdateManyAsync(effectiveSession, filter, update, options, cancellationToken).ConfigureAwait(false)
                : await mongoCollection.UpdateManyAsync(filter, update, options, cancellationToken).ConfigureAwait(false);
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
            using var _ = EnterWriteOperation();
            if (IsDryRun())
            {
                SimulateWrite(filter, update);
                return new UpdateResult.Acknowledged(1, 1, null);
            }
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

        public async Task<UpdateResult> UpdateOneAsync(
            IClientSessionHandle? session,
            FilterDefinition<TDocument> filter,
            UpdateDefinition<TDocument> update,
            UpdateOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            using var _ = EnterWriteOperation();
            if (IsDryRun())
            {
                SimulateWrite(filter, update);
                return new UpdateResult.Acknowledged(1, 1, null);
            }
            return (session ?? TryGetAmbientSession()) is { } effectiveSession
                ? await mongoCollection.UpdateOneAsync(effectiveSession, filter, update, options, cancellationToken).ConfigureAwait(false)
                : await mongoCollection.UpdateOneAsync(filter, update, options, cancellationToken).ConfigureAwait(false);
        }

        public IChangeStreamCursor<TResult> Watch<TResult>(
            PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
            ChangeStreamOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            using var _ = EnterReadOperation();
            return mongoCollection.Watch(pipeline, options, cancellationToken);
        }

        public IChangeStreamCursor<TResult> Watch<TResult>(
            IClientSessionHandle session,
            PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
            ChangeStreamOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            using var _ = EnterReadOperation();
            return mongoCollection.Watch(session, pipeline, options, cancellationToken);
        }

        public async Task<IChangeStreamCursor<TResult>> WatchAsync<TResult>(
            PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
            ChangeStreamOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            using var _ = EnterReadOperation();
            return await mongoCollection.WatchAsync(pipeline, options, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IChangeStreamCursor<TResult>> WatchAsync<TResult>(
            IClientSessionHandle session,
            PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
            ChangeStreamOptions? options = null,
            CancellationToken cancellationToken = new())
        {
            using var _ = EnterReadOperation();
            return await mongoCollection.WatchAsync(session, pipeline, options, cancellationToken).ConfigureAwait(false);
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
        /* The driver executes an aggregation whose rendered pipeline ends in a $out or
         * $merge stage as a write into the named collection: detect it with the same
         * signal, rendering the pipeline and inspecting its last stage, and guard it as
         * an aggregate to collection. Any other pipeline is a pure read. */
        private InFlightOperationScope EnterAggregateOperation<TResult>(PipelineDefinition<TDocument, TResult> pipeline)
        {
            ArgumentNullException.ThrowIfNull(pipeline);

            var renderArgs = new RenderArgs<TDocument>(mongoCollection.DocumentSerializer, dbContextEngine.SerializerRegistry);
            var lastStageName = pipeline.Render(renderArgs).Documents.LastOrDefault()?.GetElement(0).Name;
            return lastStageName is "$out" or "$merge"
                ? EnterWriteOperation("Aggregate to collection")
                : EnterReadOperation();
        }

        private InFlightOperationScope EnterIndexWriteOperation() =>
            EnterWriteOperation("Index management");

        /* Counting enters before the exclusive flags read: an operation admitted with the
         * flags off is always visible to the drain of the window flipping them, so it
         * either gets denied or gets drained — never runs unseen beside the exclusive
         * work. The same flag read that admits the operation decides whether it counts:
         * denied operations and the ones admitted by an exclusive access allowance (the
         * flows meant to work during the window) exit right away. */
        private protected InFlightOperationScope EnterReadOperation()
        {
            inFlightOperations.EnterRead();
            var isCounted = true;
            if (dbContextEngine.IsExclusiveReadEnabled)
            {
                inFlightOperations.ExitRead();
                if (!ExclusiveAccessHandler.IsExclusiveAccessAllowed(dbContextEngine))
                    throw new UnauthorizedAccessException("Read access is not allowed");
                isCounted = false;
            }
            return new InFlightOperationScope(inFlightOperations, isWriteOperation: false, isCounted);
        }

        private InFlightOperationScope EnterWriteOperation(string? dryRunDeniedOperation = null)
        {
            if (isReadOnly)
                throw new UnauthorizedAccessException("Collection is read only");

            inFlightOperations.EnterWrite();
            var isCounted = true;
            if (dbContextEngine.IsExclusiveWriteEnabled)
            {
                inFlightOperations.ExitWrite();
                if (!ExclusiveAccessHandler.IsExclusiveAccessAllowed(dbContextEngine))
                    throw new UnauthorizedAccessException("Write access is not allowed");
                isCounted = false;
            }
            var scope = new InFlightOperationScope(inFlightOperations, isWriteOperation: true, isCounted);

            if (dryRunDeniedOperation is not null)
            {
                try
                {
                    VerifyDryRunSimulable(dryRunDeniedOperation);
                }
                catch
                {
                    scope.Dispose();
                    throw;
                }
            }
            return scope;
        }

        private bool IsDryRun() =>
            DryRunHandler.IsDryRunEnabled(dbContextEngine.ExecutionContext);

        private BulkWriteResult<TDocument> SimulateBulkWrite(IEnumerable<WriteModel<TDocument>> requests)
        {
            var requestsList = requests.ToList();
            foreach (var request in requestsList)
            {
                switch (request)
                {
                    case DeleteManyModel<TDocument> deleteMany: SimulateWrite(deleteMany.Filter); break;
                    case DeleteOneModel<TDocument> deleteOne: SimulateWrite(deleteOne.Filter); break;
                    case InsertOneModel<TDocument> insertOne: SimulateWrite(documents: [insertOne.Document]); break;
                    case ReplaceOneModel<TDocument> replaceOne: SimulateWrite(replaceOne.Filter, documents: [replaceOne.Replacement]); break;
                    case UpdateManyModel<TDocument> updateMany: SimulateWrite(updateMany.Filter, updateMany.Update); break;
                    case UpdateOneModel<TDocument> updateOne: SimulateWrite(updateOne.Filter, updateOne.Update); break;
                    default: throw new InvalidOperationException($"Write model {request.GetType().Name} can't be simulated by a dry run");
                }
            }
            return new BulkWriteResult<TDocument>.Acknowledged(requestsList.Count, 0, 0, 0, 0, requestsList, []);
        }

        /* Execute the client side work of a write, exactly as the real operation would
         * before sending the command to the server, then discard it. */
        private void SimulateWrite(
            FilterDefinition<TDocument>? filter = null,
            UpdateDefinition<TDocument>? update = null,
            IEnumerable<TDocument>? documents = null)
        {
            var renderArgs = new RenderArgs<TDocument>(mongoCollection.DocumentSerializer, dbContextEngine.SerializerRegistry);
            filter?.Render(renderArgs);
            update?.Render(renderArgs);
            foreach (var document in documents ?? [])
            {
                var bsonDocument = new BsonDocument();
                using var bsonWriter = new BsonDocumentWriter(bsonDocument);
                var context = BsonSerializationContext.CreateRoot(bsonWriter);
                mongoCollection.DocumentSerializer.Serialize(context, document);
            }
        }

        private IClientSessionHandle? TryGetAmbientSession() =>
            DbSessionHandler.TryGetCurrentSession(dbContextEngine);

        private void VerifyDryRunSimulable(string operationDescription)
        {
            if (IsDryRun())
                throw new InvalidOperationException($"{operationDescription} can't be simulated by a dry run");
        }
    }
}
