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
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core.Utility
{
    /* The database handed out by a guarded collection. Collection retrieval returns
     * collections guarded by the same engine and read-only flag; database level writes
     * (collection creations, drops and renames, view creations, aggregations to
     * collection, and commands, whose arbitrary content can write) verify the write
     * permission of the originating collection and can't be simulated by a dry run;
     * reads verify the read permission. Every member enters an operation scope counting
     * the operation in flight on the engine until it completes, like the guarded
     * collections do: the exclusive access window drains the counted operations before
     * starting its work. Sessions pass verbatim without ambient resolution:
     * transactional work runs through the retrieved guarded collections, that enlist
     * their operations in the ambient session of the engine. The Client property hands
     * out the raw driver client: the explicit escape hatch out of the guarded surface,
     * like IDbContextEngine.Client. */
    internal sealed class LimitedAccessMongoDatabase(
        IDbContextEngine dbContextEngine,
        IMongoDatabase database,
        bool isReadOnly)
        : IMongoDatabase
    {
        // Fields.
        private readonly InFlightOperationsCounter inFlightOperations =
            ((IInternalDbContextEngine)dbContextEngine).InFlightOperations;

        // Properties.
        public IMongoClient Client
        {
            get
            {
                using var _ = EnterReadOperation();
                return database.Client;
            }
        }
        public DatabaseNamespace DatabaseNamespace
        {
            get
            {
                using var _ = EnterReadOperation();
                return database.DatabaseNamespace;
            }
        }
        public MongoDatabaseSettings Settings
        {
            get
            {
                using var _ = EnterReadOperation();
                return database.Settings;
            }
        }

        // Methods.
        public IAsyncCursor<TResult> Aggregate<TResult>(
            PipelineDefinition<NoPipelineInput, TResult> pipeline,
            AggregateOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterAggregateOperation(pipeline);
            return database.Aggregate(pipeline, options, cancellationToken);
        }

        public IAsyncCursor<TResult> Aggregate<TResult>(
            IClientSessionHandle session,
            PipelineDefinition<NoPipelineInput, TResult> pipeline,
            AggregateOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterAggregateOperation(pipeline);
            return database.Aggregate(session, pipeline, options, cancellationToken);
        }

        public async Task<IAsyncCursor<TResult>> AggregateAsync<TResult>(
            PipelineDefinition<NoPipelineInput, TResult> pipeline,
            AggregateOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterAggregateOperation(pipeline);
            return await database.AggregateAsync(pipeline, options, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IAsyncCursor<TResult>> AggregateAsync<TResult>(
            IClientSessionHandle session,
            PipelineDefinition<NoPipelineInput, TResult> pipeline,
            AggregateOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterAggregateOperation(pipeline);
            return await database.AggregateAsync(session, pipeline, options, cancellationToken).ConfigureAwait(false);
        }

        public void AggregateToCollection<TResult>(
            PipelineDefinition<NoPipelineInput, TResult> pipeline,
            AggregateOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterWriteOperation("Aggregate to collection");
            database.AggregateToCollection(pipeline, options, cancellationToken);
        }

        public void AggregateToCollection<TResult>(
            IClientSessionHandle session,
            PipelineDefinition<NoPipelineInput, TResult> pipeline,
            AggregateOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterWriteOperation("Aggregate to collection");
            database.AggregateToCollection(session, pipeline, options, cancellationToken);
        }

        public async Task AggregateToCollectionAsync<TResult>(
            PipelineDefinition<NoPipelineInput, TResult> pipeline,
            AggregateOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterWriteOperation("Aggregate to collection");
            await database.AggregateToCollectionAsync(pipeline, options, cancellationToken).ConfigureAwait(false);
        }

        public async Task AggregateToCollectionAsync<TResult>(
            IClientSessionHandle session,
            PipelineDefinition<NoPipelineInput, TResult> pipeline,
            AggregateOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterWriteOperation("Aggregate to collection");
            await database.AggregateToCollectionAsync(session, pipeline, options, cancellationToken).ConfigureAwait(false);
        }

        public void CreateCollection(
            string name,
            CreateCollectionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterWriteOperation("Collection creation");
            database.CreateCollection(name, options, cancellationToken);
        }

        public void CreateCollection(
            IClientSessionHandle session,
            string name,
            CreateCollectionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterWriteOperation("Collection creation");
            database.CreateCollection(session, name, options, cancellationToken);
        }

        public async Task CreateCollectionAsync(
            string name,
            CreateCollectionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterWriteOperation("Collection creation");
            await database.CreateCollectionAsync(name, options, cancellationToken).ConfigureAwait(false);
        }

        public async Task CreateCollectionAsync(
            IClientSessionHandle session,
            string name,
            CreateCollectionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterWriteOperation("Collection creation");
            await database.CreateCollectionAsync(session, name, options, cancellationToken).ConfigureAwait(false);
        }

        public void CreateView<TDocument, TResult>(
            string viewName,
            string viewOn,
            PipelineDefinition<TDocument, TResult> pipeline,
            CreateViewOptions<TDocument>? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterWriteOperation("View creation");
            database.CreateView(viewName, viewOn, pipeline, options, cancellationToken);
        }

        public void CreateView<TDocument, TResult>(
            IClientSessionHandle session,
            string viewName,
            string viewOn,
            PipelineDefinition<TDocument, TResult> pipeline,
            CreateViewOptions<TDocument>? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterWriteOperation("View creation");
            database.CreateView(session, viewName, viewOn, pipeline, options, cancellationToken);
        }

        public async Task CreateViewAsync<TDocument, TResult>(
            string viewName,
            string viewOn,
            PipelineDefinition<TDocument, TResult> pipeline,
            CreateViewOptions<TDocument>? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterWriteOperation("View creation");
            await database.CreateViewAsync(viewName, viewOn, pipeline, options, cancellationToken).ConfigureAwait(false);
        }

        public async Task CreateViewAsync<TDocument, TResult>(
            IClientSessionHandle session,
            string viewName,
            string viewOn,
            PipelineDefinition<TDocument, TResult> pipeline,
            CreateViewOptions<TDocument>? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterWriteOperation("View creation");
            await database.CreateViewAsync(session, viewName, viewOn, pipeline, options, cancellationToken).ConfigureAwait(false);
        }

        public void DropCollection(
            string name,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterWriteOperation("Collection drop");
            database.DropCollection(name, cancellationToken);
        }

        public void DropCollection(
            string name,
            DropCollectionOptions options,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterWriteOperation("Collection drop");
            database.DropCollection(name, options, cancellationToken);
        }

        public void DropCollection(
            IClientSessionHandle session,
            string name,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterWriteOperation("Collection drop");
            database.DropCollection(session, name, cancellationToken);
        }

        public void DropCollection(
            IClientSessionHandle session,
            string name,
            DropCollectionOptions options,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterWriteOperation("Collection drop");
            database.DropCollection(session, name, options, cancellationToken);
        }

        public async Task DropCollectionAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterWriteOperation("Collection drop");
            await database.DropCollectionAsync(name, cancellationToken).ConfigureAwait(false);
        }

        public async Task DropCollectionAsync(
            string name,
            DropCollectionOptions options,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterWriteOperation("Collection drop");
            await database.DropCollectionAsync(name, options, cancellationToken).ConfigureAwait(false);
        }

        public async Task DropCollectionAsync(
            IClientSessionHandle session,
            string name,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterWriteOperation("Collection drop");
            await database.DropCollectionAsync(session, name, cancellationToken).ConfigureAwait(false);
        }

        public async Task DropCollectionAsync(
            IClientSessionHandle session,
            string name,
            DropCollectionOptions options,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterWriteOperation("Collection drop");
            await database.DropCollectionAsync(session, name, options, cancellationToken).ConfigureAwait(false);
        }

        public IMongoCollection<TDocument> GetCollection<TDocument>(
            string name,
            MongoCollectionSettings? settings = null) =>
            new LimitedAccessMongoCollection<TDocument>(
                dbContextEngine,
                database.GetCollection<TDocument>(name, settings),
                isReadOnly);

        public IAsyncCursor<string> ListCollectionNames(
            ListCollectionNamesOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterReadOperation();
            return database.ListCollectionNames(options, cancellationToken);
        }

        public IAsyncCursor<string> ListCollectionNames(
            IClientSessionHandle session,
            ListCollectionNamesOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterReadOperation();
            return database.ListCollectionNames(session, options, cancellationToken);
        }

        public async Task<IAsyncCursor<string>> ListCollectionNamesAsync(
            ListCollectionNamesOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterReadOperation();
            return await database.ListCollectionNamesAsync(options, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IAsyncCursor<string>> ListCollectionNamesAsync(
            IClientSessionHandle session,
            ListCollectionNamesOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterReadOperation();
            return await database.ListCollectionNamesAsync(session, options, cancellationToken).ConfigureAwait(false);
        }

        public IAsyncCursor<BsonDocument> ListCollections(
            ListCollectionsOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterReadOperation();
            return database.ListCollections(options, cancellationToken);
        }

        public IAsyncCursor<BsonDocument> ListCollections(
            IClientSessionHandle session,
            ListCollectionsOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterReadOperation();
            return database.ListCollections(session, options, cancellationToken);
        }

        public async Task<IAsyncCursor<BsonDocument>> ListCollectionsAsync(
            ListCollectionsOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterReadOperation();
            return await database.ListCollectionsAsync(options, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IAsyncCursor<BsonDocument>> ListCollectionsAsync(
            IClientSessionHandle session,
            ListCollectionsOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterReadOperation();
            return await database.ListCollectionsAsync(session, options, cancellationToken).ConfigureAwait(false);
        }

        public void RenameCollection(
            string oldName,
            string newName,
            RenameCollectionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterWriteOperation("Collection rename");
            database.RenameCollection(oldName, newName, options, cancellationToken);
        }

        public void RenameCollection(
            IClientSessionHandle session,
            string oldName,
            string newName,
            RenameCollectionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterWriteOperation("Collection rename");
            database.RenameCollection(session, oldName, newName, options, cancellationToken);
        }

        public async Task RenameCollectionAsync(
            string oldName,
            string newName,
            RenameCollectionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterWriteOperation("Collection rename");
            await database.RenameCollectionAsync(oldName, newName, options, cancellationToken).ConfigureAwait(false);
        }

        public async Task RenameCollectionAsync(
            IClientSessionHandle session,
            string oldName,
            string newName,
            RenameCollectionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterWriteOperation("Collection rename");
            await database.RenameCollectionAsync(session, oldName, newName, options, cancellationToken).ConfigureAwait(false);
        }

        public TResult RunCommand<TResult>(
            Command<TResult> command,
            ReadPreference? readPreference = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterWriteOperation("Database command");
            return database.RunCommand(command, readPreference, cancellationToken);
        }

        public TResult RunCommand<TResult>(
            IClientSessionHandle session,
            Command<TResult> command,
            ReadPreference? readPreference = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterWriteOperation("Database command");
            return database.RunCommand(session, command, readPreference, cancellationToken);
        }

        public async Task<TResult> RunCommandAsync<TResult>(
            Command<TResult> command,
            ReadPreference? readPreference = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterWriteOperation("Database command");
            return await database.RunCommandAsync(command, readPreference, cancellationToken).ConfigureAwait(false);
        }

        public async Task<TResult> RunCommandAsync<TResult>(
            IClientSessionHandle session,
            Command<TResult> command,
            ReadPreference? readPreference = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterWriteOperation("Database command");
            return await database.RunCommandAsync(session, command, readPreference, cancellationToken).ConfigureAwait(false);
        }

        public IChangeStreamCursor<TResult> Watch<TResult>(
            PipelineDefinition<ChangeStreamDocument<BsonDocument>, TResult> pipeline,
            ChangeStreamOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterReadOperation();
            return database.Watch(pipeline, options, cancellationToken);
        }

        public IChangeStreamCursor<TResult> Watch<TResult>(
            IClientSessionHandle session,
            PipelineDefinition<ChangeStreamDocument<BsonDocument>, TResult> pipeline,
            ChangeStreamOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterReadOperation();
            return database.Watch(session, pipeline, options, cancellationToken);
        }

        public async Task<IChangeStreamCursor<TResult>> WatchAsync<TResult>(
            PipelineDefinition<ChangeStreamDocument<BsonDocument>, TResult> pipeline,
            ChangeStreamOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterReadOperation();
            return await database.WatchAsync(pipeline, options, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IChangeStreamCursor<TResult>> WatchAsync<TResult>(
            IClientSessionHandle session,
            PipelineDefinition<ChangeStreamDocument<BsonDocument>, TResult> pipeline,
            ChangeStreamOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = EnterReadOperation();
            return await database.WatchAsync(session, pipeline, options, cancellationToken).ConfigureAwait(false);
        }

        public IMongoDatabase WithReadConcern(ReadConcern readConcern) =>
            new LimitedAccessMongoDatabase(dbContextEngine, database.WithReadConcern(readConcern), isReadOnly);

        public IMongoDatabase WithReadPreference(ReadPreference readPreference) =>
            new LimitedAccessMongoDatabase(dbContextEngine, database.WithReadPreference(readPreference), isReadOnly);

        public IMongoDatabase WithWriteConcern(WriteConcern writeConcern) =>
            new LimitedAccessMongoDatabase(dbContextEngine, database.WithWriteConcern(writeConcern), isReadOnly);

        // Helpers.
        private InFlightOperationScope EnterAggregateOperation<TResult>(PipelineDefinition<NoPipelineInput, TResult> pipeline)
        {
            ArgumentNullException.ThrowIfNull(pipeline);

            /* Classify by the same signal the driver uses, like the collection level aggregates:
             * a pipeline ending in $out or $merge makes the server write the results into a
             * named collection, possibly of another database. */
            /* The pipeline input of a database level aggregate is a driver type, resolved on
             * the driver registry: it only renders the typed expressions of the stages, and
             * can't change the operator names this classification reads. */
            var renderArgs = new RenderArgs<NoPipelineInput>(
                BsonSerializer.LookupSerializer<NoPipelineInput>(),
                dbContextEngine.SerializerRegistry);
            var lastStageName = pipeline.Render(renderArgs).Documents.LastOrDefault()?.GetElement(0).Name;
            return lastStageName is "$out" or "$merge"
                ? EnterWriteOperation("Aggregate to collection")
                : EnterReadOperation();
        }

        /* Counting enters before the exclusive flags read, and the same flag read that
         * admits the operation decides whether it counts, like on the guarded
         * collections: denied operations and the ones admitted by an exclusive access
         * allowance exit right away. */
        private InFlightOperationScope EnterReadOperation()
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

        private InFlightOperationScope EnterWriteOperation(string dryRunDeniedOperation)
        {
            if (isReadOnly)
                throw new UnauthorizedAccessException("Database is read only");

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

            try
            {
                VerifyDryRunSimulable(dryRunDeniedOperation);
            }
            catch
            {
                scope.Dispose();
                throw;
            }
            return scope;
        }

        private void VerifyDryRunSimulable(string operationDescription)
        {
            if (DryRunHandler.IsDryRunEnabled(dbContextEngine.ExecutionContext))
                throw new InvalidOperationException($"{operationDescription} can't be simulated by a dry run");
        }
    }
}
