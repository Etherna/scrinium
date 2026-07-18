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

using Etherna.MongoDB.Driver;
using Etherna.MongODM.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core.Repositories
{
    /// <summary>
    /// Decoy repository bound to the proxy models created during the schema discovery,
    /// at engine initialization. It doesn't support any db operation: discovery instances
    /// only exist to generate the proxy model types.
    /// </summary>
    internal sealed class SchemaDiscoveryRepository : IRepository
    {
        // Consts.
        private const string NotSupportedMessage = "Schema discovery repository doesn't support db operations";

        // Constructors.
        private SchemaDiscoveryRepository() { }

        // Static properties.
        public static SchemaDiscoveryRepository Instance { get; } = new();

        // Properties.
        public IDbContext DbContext => throw new NotSupportedException(NotSupportedMessage);
        public bool IsInitialized => true;
        public Type KeyType => typeof(object);
        public Type ModelType => typeof(object);
        public string Name => "schemaDiscovery";

        // Methods.
        public Task BuildNewIndexesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(NotSupportedMessage);

        public Task CreateAsync(object model, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(NotSupportedMessage);

        public Task CreateAsync(IEnumerable<object> models, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(NotSupportedMessage);

        public Task DeleteAsync(IEntityModel model, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(NotSupportedMessage);

        public Task DeleteOldIndexesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(NotSupportedMessage);

        public Task<object> FindOneAsync(object id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(NotSupportedMessage);

        public void Initialize(IDbContext dbContext, ILogger logger) =>
            throw new NotSupportedException(NotSupportedMessage);

        public string ModelIdToString(object model) =>
            throw new NotSupportedException(NotSupportedMessage);

        public Task ReplaceAsync(object model, bool updateDependentDocuments = true, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(NotSupportedMessage);

        public Task ReplaceAsync(object model, IClientSessionHandle session, bool updateDependentDocuments = true, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(NotSupportedMessage);

        public Task SaveChangesAsync(IEntityModel model, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(NotSupportedMessage);

        public Task<object?> TryFindOneAsync(object id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(NotSupportedMessage);
    }
}
