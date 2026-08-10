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
     * reads verify the read permission. Sessions pass verbatim without ambient
     * resolution: transactional work runs through the retrieved guarded collections,
     * that enlist their operations in the ambient session of the engine. The Client
     * property hands out the raw driver client: the explicit escape hatch out of the
     * guarded surface, like IDbContextEngine.Client. */
    internal sealed class LimitedAccessMongoDatabase(
        IDbContextEngine dbContextEngine,
        IMongoDatabase database,
        bool isReadOnly)
        : IMongoDatabase
    {
        // Properties.
        public IMongoClient Client
        {
            get
            {
                VerifyReadPermission();
                return database.Client;
            }
        }
        public DatabaseNamespace DatabaseNamespace
        {
            get
            {
                VerifyReadPermission();
                return database.DatabaseNamespace;
            }
        }
        public MongoDatabaseSettings Settings
        {
            get
            {
                VerifyReadPermission();
                return database.Settings;
            }
        }

        // Methods.
        public IAsyncCursor<TResult> Aggregate<TResult>(
            PipelineDefinition<NoPipelineInput, TResult> pipeline,
            AggregateOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyAggregatePermission(pipeline);
            return database.Aggregate(pipeline, options, cancellationToken);
        }

        public IAsyncCursor<TResult> Aggregate<TResult>(
            IClientSessionHandle session,
            PipelineDefinition<NoPipelineInput, TResult> pipeline,
            AggregateOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyAggregatePermission(pipeline);
            return database.Aggregate(session, pipeline, options, cancellationToken);
        }

        public Task<IAsyncCursor<TResult>> AggregateAsync<TResult>(
            PipelineDefinition<NoPipelineInput, TResult> pipeline,
            AggregateOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyAggregatePermission(pipeline);
            return database.AggregateAsync(pipeline, options, cancellationToken);
        }

        public Task<IAsyncCursor<TResult>> AggregateAsync<TResult>(
            IClientSessionHandle session,
            PipelineDefinition<NoPipelineInput, TResult> pipeline,
            AggregateOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyAggregatePermission(pipeline);
            return database.AggregateAsync(session, pipeline, options, cancellationToken);
        }

        public void AggregateToCollection<TResult>(
            PipelineDefinition<NoPipelineInput, TResult> pipeline,
            AggregateOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyWritePermission();
            VerifyDryRunSimulable("Aggregate to collection");
            database.AggregateToCollection(pipeline, options, cancellationToken);
        }

        public void AggregateToCollection<TResult>(
            IClientSessionHandle session,
            PipelineDefinition<NoPipelineInput, TResult> pipeline,
            AggregateOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyWritePermission();
            VerifyDryRunSimulable("Aggregate to collection");
            database.AggregateToCollection(session, pipeline, options, cancellationToken);
        }

        public Task AggregateToCollectionAsync<TResult>(
            PipelineDefinition<NoPipelineInput, TResult> pipeline,
            AggregateOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyWritePermission();
            VerifyDryRunSimulable("Aggregate to collection");
            return database.AggregateToCollectionAsync(pipeline, options, cancellationToken);
        }

        public Task AggregateToCollectionAsync<TResult>(
            IClientSessionHandle session,
            PipelineDefinition<NoPipelineInput, TResult> pipeline,
            AggregateOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyWritePermission();
            VerifyDryRunSimulable("Aggregate to collection");
            return database.AggregateToCollectionAsync(session, pipeline, options, cancellationToken);
        }

        public void CreateCollection(
            string name,
            CreateCollectionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyWritePermission();
            VerifyDryRunSimulable("Collection creation");
            database.CreateCollection(name, options, cancellationToken);
        }

        public void CreateCollection(
            IClientSessionHandle session,
            string name,
            CreateCollectionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyWritePermission();
            VerifyDryRunSimulable("Collection creation");
            database.CreateCollection(session, name, options, cancellationToken);
        }

        public Task CreateCollectionAsync(
            string name,
            CreateCollectionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyWritePermission();
            VerifyDryRunSimulable("Collection creation");
            return database.CreateCollectionAsync(name, options, cancellationToken);
        }

        public Task CreateCollectionAsync(
            IClientSessionHandle session,
            string name,
            CreateCollectionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyWritePermission();
            VerifyDryRunSimulable("Collection creation");
            return database.CreateCollectionAsync(session, name, options, cancellationToken);
        }

        public void CreateView<TDocument, TResult>(
            string viewName,
            string viewOn,
            PipelineDefinition<TDocument, TResult> pipeline,
            CreateViewOptions<TDocument>? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyWritePermission();
            VerifyDryRunSimulable("View creation");
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
            VerifyWritePermission();
            VerifyDryRunSimulable("View creation");
            database.CreateView(session, viewName, viewOn, pipeline, options, cancellationToken);
        }

        public Task CreateViewAsync<TDocument, TResult>(
            string viewName,
            string viewOn,
            PipelineDefinition<TDocument, TResult> pipeline,
            CreateViewOptions<TDocument>? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyWritePermission();
            VerifyDryRunSimulable("View creation");
            return database.CreateViewAsync(viewName, viewOn, pipeline, options, cancellationToken);
        }

        public Task CreateViewAsync<TDocument, TResult>(
            IClientSessionHandle session,
            string viewName,
            string viewOn,
            PipelineDefinition<TDocument, TResult> pipeline,
            CreateViewOptions<TDocument>? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyWritePermission();
            VerifyDryRunSimulable("View creation");
            return database.CreateViewAsync(session, viewName, viewOn, pipeline, options, cancellationToken);
        }

        public void DropCollection(
            string name,
            CancellationToken cancellationToken = default)
        {
            VerifyWritePermission();
            VerifyDryRunSimulable("Collection drop");
            database.DropCollection(name, cancellationToken);
        }

        public void DropCollection(
            string name,
            DropCollectionOptions options,
            CancellationToken cancellationToken = default)
        {
            VerifyWritePermission();
            VerifyDryRunSimulable("Collection drop");
            database.DropCollection(name, options, cancellationToken);
        }

        public void DropCollection(
            IClientSessionHandle session,
            string name,
            CancellationToken cancellationToken = default)
        {
            VerifyWritePermission();
            VerifyDryRunSimulable("Collection drop");
            database.DropCollection(session, name, cancellationToken);
        }

        public void DropCollection(
            IClientSessionHandle session,
            string name,
            DropCollectionOptions options,
            CancellationToken cancellationToken = default)
        {
            VerifyWritePermission();
            VerifyDryRunSimulable("Collection drop");
            database.DropCollection(session, name, options, cancellationToken);
        }

        public Task DropCollectionAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            VerifyWritePermission();
            VerifyDryRunSimulable("Collection drop");
            return database.DropCollectionAsync(name, cancellationToken);
        }

        public Task DropCollectionAsync(
            string name,
            DropCollectionOptions options,
            CancellationToken cancellationToken = default)
        {
            VerifyWritePermission();
            VerifyDryRunSimulable("Collection drop");
            return database.DropCollectionAsync(name, options, cancellationToken);
        }

        public Task DropCollectionAsync(
            IClientSessionHandle session,
            string name,
            CancellationToken cancellationToken = default)
        {
            VerifyWritePermission();
            VerifyDryRunSimulable("Collection drop");
            return database.DropCollectionAsync(session, name, cancellationToken);
        }

        public Task DropCollectionAsync(
            IClientSessionHandle session,
            string name,
            DropCollectionOptions options,
            CancellationToken cancellationToken = default)
        {
            VerifyWritePermission();
            VerifyDryRunSimulable("Collection drop");
            return database.DropCollectionAsync(session, name, options, cancellationToken);
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
            VerifyReadPermission();
            return database.ListCollectionNames(options, cancellationToken);
        }

        public IAsyncCursor<string> ListCollectionNames(
            IClientSessionHandle session,
            ListCollectionNamesOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyReadPermission();
            return database.ListCollectionNames(session, options, cancellationToken);
        }

        public Task<IAsyncCursor<string>> ListCollectionNamesAsync(
            ListCollectionNamesOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyReadPermission();
            return database.ListCollectionNamesAsync(options, cancellationToken);
        }

        public Task<IAsyncCursor<string>> ListCollectionNamesAsync(
            IClientSessionHandle session,
            ListCollectionNamesOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyReadPermission();
            return database.ListCollectionNamesAsync(session, options, cancellationToken);
        }

        public IAsyncCursor<BsonDocument> ListCollections(
            ListCollectionsOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyReadPermission();
            return database.ListCollections(options, cancellationToken);
        }

        public IAsyncCursor<BsonDocument> ListCollections(
            IClientSessionHandle session,
            ListCollectionsOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyReadPermission();
            return database.ListCollections(session, options, cancellationToken);
        }

        public Task<IAsyncCursor<BsonDocument>> ListCollectionsAsync(
            ListCollectionsOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyReadPermission();
            return database.ListCollectionsAsync(options, cancellationToken);
        }

        public Task<IAsyncCursor<BsonDocument>> ListCollectionsAsync(
            IClientSessionHandle session,
            ListCollectionsOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyReadPermission();
            return database.ListCollectionsAsync(session, options, cancellationToken);
        }

        public void RenameCollection(
            string oldName,
            string newName,
            RenameCollectionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyWritePermission();
            VerifyDryRunSimulable("Collection rename");
            database.RenameCollection(oldName, newName, options, cancellationToken);
        }

        public void RenameCollection(
            IClientSessionHandle session,
            string oldName,
            string newName,
            RenameCollectionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyWritePermission();
            VerifyDryRunSimulable("Collection rename");
            database.RenameCollection(session, oldName, newName, options, cancellationToken);
        }

        public Task RenameCollectionAsync(
            string oldName,
            string newName,
            RenameCollectionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyWritePermission();
            VerifyDryRunSimulable("Collection rename");
            return database.RenameCollectionAsync(oldName, newName, options, cancellationToken);
        }

        public Task RenameCollectionAsync(
            IClientSessionHandle session,
            string oldName,
            string newName,
            RenameCollectionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyWritePermission();
            VerifyDryRunSimulable("Collection rename");
            return database.RenameCollectionAsync(session, oldName, newName, options, cancellationToken);
        }

        public TResult RunCommand<TResult>(
            Command<TResult> command,
            ReadPreference? readPreference = null,
            CancellationToken cancellationToken = default)
        {
            VerifyWritePermission();
            VerifyDryRunSimulable("Database command");
            return database.RunCommand(command, readPreference, cancellationToken);
        }

        public TResult RunCommand<TResult>(
            IClientSessionHandle session,
            Command<TResult> command,
            ReadPreference? readPreference = null,
            CancellationToken cancellationToken = default)
        {
            VerifyWritePermission();
            VerifyDryRunSimulable("Database command");
            return database.RunCommand(session, command, readPreference, cancellationToken);
        }

        public Task<TResult> RunCommandAsync<TResult>(
            Command<TResult> command,
            ReadPreference? readPreference = null,
            CancellationToken cancellationToken = default)
        {
            VerifyWritePermission();
            VerifyDryRunSimulable("Database command");
            return database.RunCommandAsync(command, readPreference, cancellationToken);
        }

        public Task<TResult> RunCommandAsync<TResult>(
            IClientSessionHandle session,
            Command<TResult> command,
            ReadPreference? readPreference = null,
            CancellationToken cancellationToken = default)
        {
            VerifyWritePermission();
            VerifyDryRunSimulable("Database command");
            return database.RunCommandAsync(session, command, readPreference, cancellationToken);
        }

        public IChangeStreamCursor<TResult> Watch<TResult>(
            PipelineDefinition<ChangeStreamDocument<BsonDocument>, TResult> pipeline,
            ChangeStreamOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyReadPermission();
            return database.Watch(pipeline, options, cancellationToken);
        }

        public IChangeStreamCursor<TResult> Watch<TResult>(
            IClientSessionHandle session,
            PipelineDefinition<ChangeStreamDocument<BsonDocument>, TResult> pipeline,
            ChangeStreamOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyReadPermission();
            return database.Watch(session, pipeline, options, cancellationToken);
        }

        public Task<IChangeStreamCursor<TResult>> WatchAsync<TResult>(
            PipelineDefinition<ChangeStreamDocument<BsonDocument>, TResult> pipeline,
            ChangeStreamOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyReadPermission();
            return database.WatchAsync(pipeline, options, cancellationToken);
        }

        public Task<IChangeStreamCursor<TResult>> WatchAsync<TResult>(
            IClientSessionHandle session,
            PipelineDefinition<ChangeStreamDocument<BsonDocument>, TResult> pipeline,
            ChangeStreamOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            VerifyReadPermission();
            return database.WatchAsync(session, pipeline, options, cancellationToken);
        }

        public IMongoDatabase WithReadConcern(ReadConcern readConcern) =>
            new LimitedAccessMongoDatabase(dbContextEngine, database.WithReadConcern(readConcern), isReadOnly);

        public IMongoDatabase WithReadPreference(ReadPreference readPreference) =>
            new LimitedAccessMongoDatabase(dbContextEngine, database.WithReadPreference(readPreference), isReadOnly);

        public IMongoDatabase WithWriteConcern(WriteConcern writeConcern) =>
            new LimitedAccessMongoDatabase(dbContextEngine, database.WithWriteConcern(writeConcern), isReadOnly);

        // Helpers.
        private void VerifyAggregatePermission<TResult>(PipelineDefinition<NoPipelineInput, TResult> pipeline)
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
            if (lastStageName is "$out" or "$merge")
            {
                VerifyWritePermission();
                VerifyDryRunSimulable("Aggregate to collection");
            }
            else
            {
                VerifyReadPermission();
            }
        }

        private void VerifyDryRunSimulable(string operationDescription)
        {
            if (dbContextEngine.ExecutionContext.Items is not null &&
                DryRunHandler.IsDryRunEnabled(dbContextEngine.ExecutionContext))
                throw new InvalidOperationException($"{operationDescription} can't be simulated by a dry run");
        }

        private void VerifyReadPermission()
        {
            if (dbContextEngine.IsExclusiveReadEnabled &&
                !ExclusiveAccessHandler.IsExclusiveAccessAllowed(dbContextEngine.ExecutionContext))
                throw new UnauthorizedAccessException("Read access is not allowed");
        }

        private void VerifyWritePermission()
        {
            if (isReadOnly)
                throw new UnauthorizedAccessException("Database is read only");

            if (dbContextEngine.IsExclusiveWriteEnabled &&
                !ExclusiveAccessHandler.IsExclusiveAccessAllowed(dbContextEngine.ExecutionContext))
                throw new UnauthorizedAccessException("Write access is not allowed");
        }
    }
}
