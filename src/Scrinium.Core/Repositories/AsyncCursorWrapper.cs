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
using Etherna.MongODM.Core.Utility;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core.Repositories
{
    internal sealed class AsyncCursorWrapper<TProjection> : IAsyncCursor<TProjection>
    {
        // Fields.
        private readonly IAsyncCursor<TProjection> cursor;
        private readonly DbExecutionContextHandler dbExecutionContextHandler;
        private bool disposed;

        // Constructor and dispose.
        public AsyncCursorWrapper(
            IAsyncCursor<TProjection> cursor,
            DbExecutionContextHandler dbExecutionContextHandler)
        {
            this.cursor = cursor;
            this.dbExecutionContextHandler = dbExecutionContextHandler;
        }

        public void Dispose()
        {
            if (disposed) return;

            // Dispose managed resources.
            cursor.Dispose();
            dbExecutionContextHandler.Dispose();

            disposed = true;
            GC.SuppressFinalize(this);
        }

        // Properties.
        public IEnumerable<TProjection> Current => cursor.Current;

        // Methods.
        public bool MoveNext(CancellationToken cancellationToken = default) => cursor.MoveNext(cancellationToken);

        public Task<bool> MoveNextAsync(CancellationToken cancellationToken = default) => cursor.MoveNextAsync(cancellationToken);
    }
}
