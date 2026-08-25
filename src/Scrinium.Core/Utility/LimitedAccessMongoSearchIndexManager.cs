// Copyright 2020-present Etherna SA
// This file is part of Scrinium.
//
// Scrinium is free software: you can redistribute it and/or modify it under the terms of the
// GNU Lesser General Public License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
//
// Scrinium is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY;
// without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License along with Scrinium.
// If not, see <https://www.gnu.org/licenses/>.

using Etherna.MongoDB.Bson;
using Etherna.MongoDB.Driver;
using Etherna.MongoDB.Driver.Search;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.Scrinium.Core.Utility
{
    /* Search index management operations are writes on the collection: index creations,
     * drops and updates enter the write operation scope of the owning collection,
     * listings the read one, counting in flight on the engine until they complete like
     * any guarded operation. */
    internal sealed class LimitedAccessMongoSearchIndexManager(
        IMongoSearchIndexManager searchIndexManager,
        Func<InFlightOperationScope> enterReadOperation,
        Func<InFlightOperationScope> enterWriteOperation)
        : IMongoSearchIndexManager
    {
        // Methods.
        public IEnumerable<string> CreateMany(
            IEnumerable<CreateSearchIndexModel> models,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            return searchIndexManager.CreateMany(models, cancellationToken);
        }

        public async Task<IEnumerable<string>> CreateManyAsync(
            IEnumerable<CreateSearchIndexModel> models,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            return await searchIndexManager.CreateManyAsync(models, cancellationToken).ConfigureAwait(false);
        }

        public string CreateOne(
            CreateSearchIndexModel model,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            return searchIndexManager.CreateOne(model, cancellationToken);
        }

        public string CreateOne(
            BsonDocument definition,
            string? name = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            return searchIndexManager.CreateOne(definition, name, cancellationToken);
        }

        public async Task<string> CreateOneAsync(
            CreateSearchIndexModel model,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            return await searchIndexManager.CreateOneAsync(model, cancellationToken).ConfigureAwait(false);
        }

        public async Task<string> CreateOneAsync(
            BsonDocument definition,
            string? name = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            return await searchIndexManager.CreateOneAsync(definition, name, cancellationToken).ConfigureAwait(false);
        }

        public void DropOne(
            string name,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            searchIndexManager.DropOne(name, cancellationToken);
        }

        public async Task DropOneAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            await searchIndexManager.DropOneAsync(name, cancellationToken).ConfigureAwait(false);
        }

        public IAsyncCursor<BsonDocument> List(
            string? name = null,
            AggregateOptions? aggregateOptions = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterReadOperation();
            return searchIndexManager.List(name, aggregateOptions, cancellationToken);
        }

        public async Task<IAsyncCursor<BsonDocument>> ListAsync(
            string? name = null,
            AggregateOptions? aggregateOptions = null,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterReadOperation();
            return await searchIndexManager.ListAsync(name, aggregateOptions, cancellationToken).ConfigureAwait(false);
        }

        public void Update(
            string name,
            BsonDocument definition,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            searchIndexManager.Update(name, definition, cancellationToken);
        }

        public async Task UpdateAsync(
            string name,
            BsonDocument definition,
            CancellationToken cancellationToken = default)
        {
            using var _ = enterWriteOperation();
            await searchIndexManager.UpdateAsync(name, definition, cancellationToken).ConfigureAwait(false);
        }
    }
}
