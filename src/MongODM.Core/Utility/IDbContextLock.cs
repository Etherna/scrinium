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
using System.Threading.Tasks;

namespace Etherna.MongODM.Core.Utility
{
    /// <summary>
    /// Server side lock of a db context, coordinating its exclusive works (seeding and
    /// migrations) once per db context across every application instance connected to the
    /// database. The lock is a lease document claimed atomically on the server: the lease
    /// expires when its owner stops renewing it, so a dead owner unblocks new claims without
    /// any manual repair.
    /// </summary>
    /// <remarks>
    /// The lease document lives in the collection named by
    /// <see cref="Options.IDbContextOptions.DbLockCollectionName"/>: applications configuring
    /// different collection names for the same database don't exclude each other.
    /// </remarks>
    public interface IDbContextLock
    {
        /// <summary>
        /// True while an owner holds a live lease on the lock. A lease whose expiration is
        /// passed doesn't lock anything: the next claim takes it over.
        /// </summary>
        /// <returns>True if the lock is held by a live lease</returns>
        Task<bool> IsLockedAsync();

        /// <summary>
        /// Try to claim the lock for an owner, atomically on the server: with concurrent
        /// claims from any process, a single claimer wins. An expired lease is taken over.
        /// The new lease lasts a limited time without renewals: resume the claim with
        /// <see cref="TryResumeClaimAsync"/> to keep it renewed while working, or release it
        /// with <see cref="TryReleaseAsync"/>.
        /// </summary>
        /// <param name="ownerId">The claiming owner identifier</param>
        /// <param name="leaseDuration">Duration of the claimed lease, defaulted to
        /// <see cref="DbContextLock.DefaultLeaseDuration"/>. It is how long the lock stays
        /// claimed when its owner dies before releasing it, and how long the claim survives
        /// before someone resumes it into a renewed lease: it doesn't have to cover the work
        /// running under the lease, kept renewed in background. The chosen duration is
        /// persisted in the lease document, so any owner resuming the claim renews on it</param>
        /// <returns>True if the lock has been claimed, false if another owner holds it</returns>
        /// <exception cref="ArgumentOutOfRangeException">The lease duration is shorter than
        /// <see cref="DbContextLock.MinLeaseDuration"/></exception>
        Task<bool> TryClaimAsync(string ownerId, TimeSpan? leaseDuration = null);

        /// <summary>
        /// The lease resumed by an outer section of the current flow, registered on the
        /// execution context.
        /// </summary>
        /// <returns>The ambient lease, or null when the current flow doesn't hold one</returns>
        IDbContextLockLease? TryGetAmbientLease();

        /// <summary>
        /// Release a claim never resumed into a lease, permitting new claims without waiting
        /// the lease expiration. The release is owner guarded: a lock already taken over by
        /// another owner stays untouched.
        /// </summary>
        /// <param name="ownerId">The owner identifier used by the claim</param>
        Task TryReleaseAsync(string ownerId);

        /// <summary>
        /// Try to resume a claimed lock, verifying the ownership and renewing the lease. The
        /// returned lease keeps renewing in background, on the duration chosen by its claim,
        /// and is registered as ambient on the execution context, until its disposal releases
        /// the lock. Resuming stamps a fresh lease identifier: a second resume of the same
        /// owner invalidates the first lease, that loses it.
        /// </summary>
        /// <param name="ownerId">The owner identifier used by the claim</param>
        /// <returns>The active lease, or null when the owner doesn't hold the lock anymore</returns>
        Task<IDbContextLockLease?> TryResumeClaimAsync(string ownerId);
    }
}
