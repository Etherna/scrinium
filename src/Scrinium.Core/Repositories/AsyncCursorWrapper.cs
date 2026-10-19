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

using Etherna.MongoDB.Driver;
using Etherna.Scrinium.Core.Utility;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.Scrinium.Core.Repositories
{
    /// <summary>
    /// Wraps the cursor of a repository query, re-establishing the db execution context of the
    /// repository around each batch fetch: the batches after the first deserialize inside the
    /// flow consuming the cursor, after the query method returned, where its own db execution
    /// context is gone.
    /// </summary>
    internal sealed class AsyncCursorWrapper<TProjection>(
        IAsyncCursor<TProjection> cursor,
        IDbContext dbContext,
        IRepository repository)
        : IAsyncCursor<TProjection>
    {
        // Fields.
        private bool disposed;

        // Constructor and dispose.
        public void Dispose()
        {
            if (disposed) return;

            // Dispose managed resources.
            cursor.Dispose();

            disposed = true;
        }

        // Properties.
        public IEnumerable<TProjection> Current => cursor.Current;

        // Methods.
        public bool MoveNext(CancellationToken cancellationToken = default)
        {
            using var dbExecContextHandler = new DbExecutionContextHandler(dbContext, repository);
            return cursor.MoveNext(cancellationToken);
        }

        public async Task<bool> MoveNextAsync(CancellationToken cancellationToken = default)
        {
            using var dbExecContextHandler = new DbExecutionContextHandler(dbContext, repository);
            return await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
