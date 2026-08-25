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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.Scrinium.Core.Utility
{
    /* Index management operations are writes on the collection: index creations and drops
     * enter the write operation scope of the owning collection, listings and metadata
     * reads the read one, counting in flight on the engine until they complete like any
     * guarded operation. Sessions pass verbatim: index operations can't run in
     * transactions, so no ambient session resolves. */
    internal sealed class LimitedAccessMongoIndexManager<TDocument>(
        IMongoIndexManager<TDocument> indexManager,
        Func<InFlightOperationScope> enterReadOperation,
        Func<InFlightOperationScope> enterWriteOperation)
        : IMongoIndexManager<TDocument>
    {
        // Properties.
        public CollectionNamespace CollectionNamespace
        {
            get
            {
                using var _ = enterReadOperation();
                return indexManager.CollectionNamespace;
            }
        }
        public IBsonSerializer<TDocument> DocumentSerializer
        {
            get
            {
                using var _ = enterReadOperation();
                return indexManager.DocumentSerializer;
            }
        }
        public MongoCollectionSettings Settings
        {
            get
            {
                using var _ = enterReadOperation();
                return indexManager.Settings;
            }
        }

        // Methods.
        public IEnumerable<string> CreateMany(
            IEnumerable<CreateIndexModel<TDocument>> models,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            return indexManager.CreateMany(models, cancellationToken);
        }

        public IEnumerable<string> CreateMany(
            IEnumerable<CreateIndexModel<TDocument>> models,
            CreateManyIndexesOptions options,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            return indexManager.CreateMany(models, options, cancellationToken);
        }

        public IEnumerable<string> CreateMany(
            IClientSessionHandle session,
            IEnumerable<CreateIndexModel<TDocument>> models,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            return indexManager.CreateMany(session, models, cancellationToken);
        }

        public IEnumerable<string> CreateMany(
            IClientSessionHandle session,
            IEnumerable<CreateIndexModel<TDocument>> models,
            CreateManyIndexesOptions options,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            return indexManager.CreateMany(session, models, options, cancellationToken);
        }

        public async Task<IEnumerable<string>> CreateManyAsync(
            IEnumerable<CreateIndexModel<TDocument>> models,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            return await indexManager.CreateManyAsync(models, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IEnumerable<string>> CreateManyAsync(
            IEnumerable<CreateIndexModel<TDocument>> models,
            CreateManyIndexesOptions options,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            return await indexManager.CreateManyAsync(models, options, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IEnumerable<string>> CreateManyAsync(
            IClientSessionHandle session,
            IEnumerable<CreateIndexModel<TDocument>> models,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            return await indexManager.CreateManyAsync(session, models, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IEnumerable<string>> CreateManyAsync(
            IClientSessionHandle session,
            IEnumerable<CreateIndexModel<TDocument>> models,
            CreateManyIndexesOptions options,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            return await indexManager.CreateManyAsync(session, models, options, cancellationToken).ConfigureAwait(false);
        }

        public string CreateOne(
            CreateIndexModel<TDocument> model,
            CreateOneIndexOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            return indexManager.CreateOne(model, options, cancellationToken);
        }

        [Obsolete("Use CreateOne with a CreateIndexModel instead.")]
        public string CreateOne(
            IndexKeysDefinition<TDocument> keys,
            CreateIndexOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            return indexManager.CreateOne(keys, options, cancellationToken);
        }

        public string CreateOne(
            IClientSessionHandle session,
            CreateIndexModel<TDocument> model,
            CreateOneIndexOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            return indexManager.CreateOne(session, model, options, cancellationToken);
        }

        [Obsolete("Use CreateOne with a CreateIndexModel instead.")]
        public string CreateOne(
            IClientSessionHandle session,
            IndexKeysDefinition<TDocument> keys,
            CreateIndexOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            return indexManager.CreateOne(session, keys, options, cancellationToken);
        }

        public async Task<string> CreateOneAsync(
            CreateIndexModel<TDocument> model,
            CreateOneIndexOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            return await indexManager.CreateOneAsync(model, options, cancellationToken).ConfigureAwait(false);
        }

        [Obsolete("Use CreateOneAsync with a CreateIndexModel instead.")]
        public async Task<string> CreateOneAsync(
            IndexKeysDefinition<TDocument> keys,
            CreateIndexOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            return await indexManager.CreateOneAsync(keys, options, cancellationToken).ConfigureAwait(false);
        }

        public async Task<string> CreateOneAsync(
            IClientSessionHandle session,
            CreateIndexModel<TDocument> model,
            CreateOneIndexOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            return await indexManager.CreateOneAsync(session, model, options, cancellationToken).ConfigureAwait(false);
        }

        [Obsolete("Use CreateOneAsync with a CreateIndexModel instead.")]
        public async Task<string> CreateOneAsync(
            IClientSessionHandle session,
            IndexKeysDefinition<TDocument> keys,
            CreateIndexOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            return await indexManager.CreateOneAsync(session, keys, options, cancellationToken).ConfigureAwait(false);
        }

        public void DropAll(CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            indexManager.DropAll(cancellationToken);
        }

        public void DropAll(
            DropIndexOptions options,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            indexManager.DropAll(options, cancellationToken);
        }

        public void DropAll(
            IClientSessionHandle session,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            indexManager.DropAll(session, cancellationToken);
        }

        public void DropAll(
            IClientSessionHandle session,
            DropIndexOptions options,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            indexManager.DropAll(session, options, cancellationToken);
        }

        public async Task DropAllAsync(CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            await indexManager.DropAllAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task DropAllAsync(
            DropIndexOptions options,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            await indexManager.DropAllAsync(options, cancellationToken).ConfigureAwait(false);
        }

        public async Task DropAllAsync(
            IClientSessionHandle session,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            await indexManager.DropAllAsync(session, cancellationToken).ConfigureAwait(false);
        }

        public async Task DropAllAsync(
            IClientSessionHandle session,
            DropIndexOptions options,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            await indexManager.DropAllAsync(session, options, cancellationToken).ConfigureAwait(false);
        }

        public void DropOne(
            string name,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            indexManager.DropOne(name, cancellationToken);
        }

        public void DropOne(
            string name,
            DropIndexOptions options,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            indexManager.DropOne(name, options, cancellationToken);
        }

        public void DropOne(
            IClientSessionHandle session,
            string name,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            indexManager.DropOne(session, name, cancellationToken);
        }

        public void DropOne(
            IClientSessionHandle session,
            string name,
            DropIndexOptions options,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            indexManager.DropOne(session, name, options, cancellationToken);
        }

        public async Task DropOneAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            await indexManager.DropOneAsync(name, cancellationToken).ConfigureAwait(false);
        }

        public async Task DropOneAsync(
            string name,
            DropIndexOptions options,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            await indexManager.DropOneAsync(name, options, cancellationToken).ConfigureAwait(false);
        }

        public async Task DropOneAsync(
            IClientSessionHandle session,
            string name,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            await indexManager.DropOneAsync(session, name, cancellationToken).ConfigureAwait(false);
        }

        public async Task DropOneAsync(
            IClientSessionHandle session,
            string name,
            DropIndexOptions options,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            await indexManager.DropOneAsync(session, name, options, cancellationToken).ConfigureAwait(false);
        }

        public IAsyncCursor<BsonDocument> List(CancellationToken cancellationToken = default)
        {
            using var _ = enterReadOperation();
            return indexManager.List(cancellationToken);
        }

        public IAsyncCursor<BsonDocument> List(
            ListIndexesOptions options,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterReadOperation();
            return indexManager.List(options, cancellationToken);
        }

        public IAsyncCursor<BsonDocument> List(
            IClientSessionHandle session,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterReadOperation();
            return indexManager.List(session, cancellationToken);
        }

        public IAsyncCursor<BsonDocument> List(
            IClientSessionHandle session,
            ListIndexesOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterReadOperation();
            return indexManager.List(session, options, cancellationToken);
        }

        public async Task<IAsyncCursor<BsonDocument>> ListAsync(CancellationToken cancellationToken = default)
        {
            using var _ = enterReadOperation();
            return await indexManager.ListAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<IAsyncCursor<BsonDocument>> ListAsync(
            ListIndexesOptions options,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterReadOperation();
            return await indexManager.ListAsync(options, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IAsyncCursor<BsonDocument>> ListAsync(
            IClientSessionHandle session,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterReadOperation();
            return await indexManager.ListAsync(session, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IAsyncCursor<BsonDocument>> ListAsync(
            IClientSessionHandle session,
            ListIndexesOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterReadOperation();
            return await indexManager.ListAsync(session, options, cancellationToken).ConfigureAwait(false);
        }
    }
}
