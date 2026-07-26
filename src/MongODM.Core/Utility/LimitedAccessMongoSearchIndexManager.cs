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
using Etherna.MongoDB.Driver;
using Etherna.MongoDB.Driver.Search;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core.Utility
{
    /* Search index management operations are writes on the collection: index creations,
     * drops and updates verify the write permission of the owning collection, listings
     * verify the read one. */
    internal sealed class LimitedAccessMongoSearchIndexManager(
        IMongoSearchIndexManager searchIndexManager,
        Action verifyReadPermission,
        Action verifyWritePermission)
        : IMongoSearchIndexManager
    {
        // Methods.
        public IEnumerable<string> CreateMany(
            IEnumerable<CreateSearchIndexModel> models,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return searchIndexManager.CreateMany(models, cancellationToken);
        }

        public Task<IEnumerable<string>> CreateManyAsync(
            IEnumerable<CreateSearchIndexModel> models,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return searchIndexManager.CreateManyAsync(models, cancellationToken);
        }

        public string CreateOne(
            CreateSearchIndexModel model,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return searchIndexManager.CreateOne(model, cancellationToken);
        }

        public string CreateOne(
            BsonDocument definition,
            string? name = null,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return searchIndexManager.CreateOne(definition, name, cancellationToken);
        }

        public Task<string> CreateOneAsync(
            CreateSearchIndexModel model,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return searchIndexManager.CreateOneAsync(model, cancellationToken);
        }

        public Task<string> CreateOneAsync(
            BsonDocument definition,
            string? name = null,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return searchIndexManager.CreateOneAsync(definition, name, cancellationToken);
        }

        public void DropOne(
            string name,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            searchIndexManager.DropOne(name, cancellationToken);
        }

        public Task DropOneAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return searchIndexManager.DropOneAsync(name, cancellationToken);
        }

        public IAsyncCursor<BsonDocument> List(
            string? name = null,
            AggregateOptions? aggregateOptions = null,
            CancellationToken cancellationToken = default)
        {
            verifyReadPermission();
            return searchIndexManager.List(name, aggregateOptions, cancellationToken);
        }

        public Task<IAsyncCursor<BsonDocument>> ListAsync(
            string? name = null,
            AggregateOptions? aggregateOptions = null,
            CancellationToken cancellationToken = default)
        {
            verifyReadPermission();
            return searchIndexManager.ListAsync(name, aggregateOptions, cancellationToken);
        }

        public void Update(
            string name,
            BsonDocument definition,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            searchIndexManager.Update(name, definition, cancellationToken);
        }

        public Task UpdateAsync(
            string name,
            BsonDocument definition,
            CancellationToken cancellationToken = default)
        {
            verifyWritePermission();
            return searchIndexManager.UpdateAsync(name, definition, cancellationToken);
        }
    }
}
