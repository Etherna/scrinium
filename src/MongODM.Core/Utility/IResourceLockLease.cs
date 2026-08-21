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

using System;
using System.Threading;

namespace Etherna.MongODM.Core.Utility
{
    /// <summary>
    /// An active lease of a <see cref="IResourceLock"/>, renewed in background until its
    /// disposal, that releases the lock. Dispose it as soon as the work under it ends: the
    /// background renewals keep the lease alive as long as the lease object lives, so a
    /// lease never disposed holds its lock indefinitely, denying every work coordinated by
    /// it on every application instance connected to the database.
    /// </summary>
    public interface IResourceLockLease : IAsyncDisposable
    {
        /// <summary>
        /// Cancelled when the lease can't be assumed alive anymore: a renewal found the lock
        /// taken over by another claimer (or invalidated by another resume of the same
        /// owner), or the renewals kept failing for the whole lease duration. Long running
        /// work under the lease should observe it and abort.
        /// </summary>
        CancellationToken LeaseLostToken { get; }

        /// <summary>
        /// The owner identifier holding the lease: the one of the resumed claim, or a
        /// generated identifier for an acquired lease.
        /// </summary>
        string OwnerId { get; }
    }
}
