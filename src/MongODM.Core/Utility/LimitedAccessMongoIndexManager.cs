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

namespace Etherna.MongODM.Core.Utility
{
    /* Index management operations are writes on the collection: index creations and drops
     * verify the write permission of the owning collection, listings and metadata reads
     * verify the read one. Sessions pass verbatim: index operations can't run in
     * transactions, so no ambient session resolves. */
    internal sealed class LimitedAccessMongoIndexManager<TDocument>(
        IMongoIndexManager<TDocument> indexManager,
        Action verifyReadPermission,
        Action verifyWritePermission)
        : IMongoIndexManager<TDocument>
    {
        // Properties.
        public CollectionNamespace CollectionNamespace
        {
            get
            {
                verifyReadPermission();
                return indexManager.CollectionNamespace;
            }
        }
        public IBsonSerializer<TDocument> DocumentSerializer
        {
            get
            {
                verifyReadPermission();
                return indexManager.DocumentSerializer;
            }
        }
        public MongoCollectionSettings Settings
        {
            get
            {
                verifyReadPermission();
                return indexManager.Settings;
            }
        }

        // Methods.
        public IEnumerable<string> CreateMany(
            IEnumerable<CreateIndexModel<TDocument>> models,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return indexManager.CreateMany(models, cancellationToken);
        }

        public IEnumerable<string> CreateMany(
            IEnumerable<CreateIndexModel<TDocument>> models,
            CreateManyIndexesOptions options,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return indexManager.CreateMany(models, options, cancellationToken);
        }

        public IEnumerable<string> CreateMany(
            IClientSessionHandle session,
            IEnumerable<CreateIndexModel<TDocument>> models,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return indexManager.CreateMany(session, models, cancellationToken);
        }

        public IEnumerable<string> CreateMany(
            IClientSessionHandle session,
            IEnumerable<CreateIndexModel<TDocument>> models,
            CreateManyIndexesOptions options,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return indexManager.CreateMany(session, models, options, cancellationToken);
        }

        public Task<IEnumerable<string>> CreateManyAsync(
            IEnumerable<CreateIndexModel<TDocument>> models,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return indexManager.CreateManyAsync(models, cancellationToken);
        }

        public Task<IEnumerable<string>> CreateManyAsync(
            IEnumerable<CreateIndexModel<TDocument>> models,
            CreateManyIndexesOptions options,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return indexManager.CreateManyAsync(models, options, cancellationToken);
        }

        public Task<IEnumerable<string>> CreateManyAsync(
            IClientSessionHandle session,
            IEnumerable<CreateIndexModel<TDocument>> models,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return indexManager.CreateManyAsync(session, models, cancellationToken);
        }

        public Task<IEnumerable<string>> CreateManyAsync(
            IClientSessionHandle session,
            IEnumerable<CreateIndexModel<TDocument>> models,
            CreateManyIndexesOptions options,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return indexManager.CreateManyAsync(session, models, options, cancellationToken);
        }

        public string CreateOne(
            CreateIndexModel<TDocument> model,
            CreateOneIndexOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return indexManager.CreateOne(model, options, cancellationToken);
        }

        [Obsolete("Use CreateOne with a CreateIndexModel instead.")]
        public string CreateOne(
            IndexKeysDefinition<TDocument> keys,
            CreateIndexOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return indexManager.CreateOne(keys, options, cancellationToken);
        }

        public string CreateOne(
            IClientSessionHandle session,
            CreateIndexModel<TDocument> model,
            CreateOneIndexOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return indexManager.CreateOne(session, model, options, cancellationToken);
        }

        [Obsolete("Use CreateOne with a CreateIndexModel instead.")]
        public string CreateOne(
            IClientSessionHandle session,
            IndexKeysDefinition<TDocument> keys,
            CreateIndexOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return indexManager.CreateOne(session, keys, options, cancellationToken);
        }

        public Task<string> CreateOneAsync(
            CreateIndexModel<TDocument> model,
            CreateOneIndexOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return indexManager.CreateOneAsync(model, options, cancellationToken);
        }

        [Obsolete("Use CreateOneAsync with a CreateIndexModel instead.")]
        public Task<string> CreateOneAsync(
            IndexKeysDefinition<TDocument> keys,
            CreateIndexOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return indexManager.CreateOneAsync(keys, options, cancellationToken);
        }

        public Task<string> CreateOneAsync(
            IClientSessionHandle session,
            CreateIndexModel<TDocument> model,
            CreateOneIndexOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return indexManager.CreateOneAsync(session, model, options, cancellationToken);
        }

        [Obsolete("Use CreateOneAsync with a CreateIndexModel instead.")]
        public Task<string> CreateOneAsync(
            IClientSessionHandle session,
            IndexKeysDefinition<TDocument> keys,
            CreateIndexOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return indexManager.CreateOneAsync(session, keys, options, cancellationToken);
        }

        public void DropAll(CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            indexManager.DropAll(cancellationToken);
        }

        public void DropAll(
            DropIndexOptions options,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            indexManager.DropAll(options, cancellationToken);
        }

        public void DropAll(
            IClientSessionHandle session,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            indexManager.DropAll(session, cancellationToken);
        }

        public void DropAll(
            IClientSessionHandle session,
            DropIndexOptions options,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            indexManager.DropAll(session, options, cancellationToken);
        }

        public Task DropAllAsync(CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return indexManager.DropAllAsync(cancellationToken);
        }

        public Task DropAllAsync(
            DropIndexOptions options,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return indexManager.DropAllAsync(options, cancellationToken);
        }

        public Task DropAllAsync(
            IClientSessionHandle session,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return indexManager.DropAllAsync(session, cancellationToken);
        }

        public Task DropAllAsync(
            IClientSessionHandle session,
            DropIndexOptions options,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return indexManager.DropAllAsync(session, options, cancellationToken);
        }

        public void DropOne(
            string name,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            indexManager.DropOne(name, cancellationToken);
        }

        public void DropOne(
            string name,
            DropIndexOptions options,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            indexManager.DropOne(name, options, cancellationToken);
        }

        public void DropOne(
            IClientSessionHandle session,
            string name,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            indexManager.DropOne(session, name, cancellationToken);
        }

        public void DropOne(
            IClientSessionHandle session,
            string name,
            DropIndexOptions options,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            indexManager.DropOne(session, name, options, cancellationToken);
        }

        public Task DropOneAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return indexManager.DropOneAsync(name, cancellationToken);
        }

        public Task DropOneAsync(
            string name,
            DropIndexOptions options,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return indexManager.DropOneAsync(name, options, cancellationToken);
        }

        public Task DropOneAsync(
            IClientSessionHandle session,
            string name,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return indexManager.DropOneAsync(session, name, cancellationToken);
        }

        public Task DropOneAsync(
            IClientSessionHandle session,
            string name,
            DropIndexOptions options,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return indexManager.DropOneAsync(session, name, options, cancellationToken);
        }

        public IAsyncCursor<BsonDocument> List(CancellationToken cancellationToken = default)
        {
            verifyReadPermission();
            return indexManager.List(cancellationToken);
        }

        public IAsyncCursor<BsonDocument> List(
            ListIndexesOptions options,
            CancellationToken cancellationToken = default)
        {
            verifyReadPermission();
            return indexManager.List(options, cancellationToken);
        }

        public IAsyncCursor<BsonDocument> List(
            IClientSessionHandle session,
            CancellationToken cancellationToken = default)
        {
            verifyReadPermission();
            return indexManager.List(session, cancellationToken);
        }

        public IAsyncCursor<BsonDocument> List(
            IClientSessionHandle session,
            ListIndexesOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            verifyReadPermission();
            return indexManager.List(session, options, cancellationToken);
        }

        public Task<IAsyncCursor<BsonDocument>> ListAsync(CancellationToken cancellationToken = default)
        {
            verifyReadPermission();
            return indexManager.ListAsync(cancellationToken);
        }

        public Task<IAsyncCursor<BsonDocument>> ListAsync(
            ListIndexesOptions options,
            CancellationToken cancellationToken = default)
        {
            verifyReadPermission();
            return indexManager.ListAsync(options, cancellationToken);
        }

        public Task<IAsyncCursor<BsonDocument>> ListAsync(
            IClientSessionHandle session,
            CancellationToken cancellationToken = default)
        {
            verifyReadPermission();
            return indexManager.ListAsync(session, cancellationToken);
        }

        public Task<IAsyncCursor<BsonDocument>> ListAsync(
            IClientSessionHandle session,
            ListIndexesOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            verifyReadPermission();
            return indexManager.ListAsync(session, options, cancellationToken);
        }
    }
}
