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

using System;
using System.Threading.Tasks;

namespace Etherna.Scrinium.Core.Utility
{
    /// <summary>
    /// Server side lease lock over a resource, coordinating works across every application
    /// instance connected to the database. The lock is a lease document claimed atomically
    /// on the server: the lease expires when its owner stops renewing it, so a dead owner
    /// unblocks new claims without any manual repair. The db context engine binds one lock
    /// to its own identifier, coordinating its seedings and migrations
    /// (<see cref="IDbContextEngine.DbContextLock"/>), and applications build the locks of
    /// their own resources with <see cref="IDbContextEngine.GetResourceLock"/>.
    /// </summary>
    /// <remarks>
    /// The lease documents live in the collection named by
    /// <see cref="Options.IDbContextOptions.DbLockCollectionName"/>: applications
    /// configuring different collection names for the same database don't exclude each
    /// other. The collection is accessed raw, out of the read-only, exclusive access and dry
    /// run limitations of the db context: the locks are the coordination infrastructure of
    /// the works those limitations serve, so they stay claimable and renewable while an
    /// exclusive access denies the collections, and real also inside a simulated flow.
    /// </remarks>
    public interface IResourceLock
    {
        /// <summary>
        /// True while a live lease holds the lock, exclusive or shared. A lease whose
        /// expiration is passed doesn't lock anything: the next claim takes it over.
        /// </summary>
        /// <returns>True if the lock is held by a live lease</returns>
        Task<bool> IsLockedAsync();

        /// <summary>
        /// Try to acquire the lock, atomically on the server: claim and renewed lease in a
        /// single command, for the flow acquiring and working in the same process (a claim
        /// resumed by another process goes through <see cref="TryClaimAsync"/> and
        /// <see cref="TryResumeClaimAsync"/> instead). An exclusive acquisition admits a
        /// single holder, denied by any live lease; a shared one admits any number of
        /// holders, each with its own renewed lease, denied only by a live exclusive lease,
        /// and a dead shared holder expires alone, without affecting the others. An expired
        /// lease is taken over. The returned lease keeps renewing in background and is
        /// registered as ambient on the execution context, until its disposal releases this
        /// holder, deleting the lock document when it was the last one.
        /// </summary>
        /// <param name="mode">The acquisition mode: a single exclusive holder, or shared
        /// holders coexisting on the resource</param>
        /// <param name="leaseDuration">Duration of the acquired lease, defaulted to
        /// <see cref="ResourceLock.DefaultLeaseDuration"/>. It is how long this holder locks
        /// the resource when its process dies before releasing it: it doesn't have to cover
        /// the work running under the lease, kept renewed in background</param>
        /// <returns>The active lease, or null when a live lease denies the acquisition</returns>
        /// <exception cref="ArgumentOutOfRangeException">The lease duration is shorter than
        /// <see cref="ResourceLock.MinLeaseDuration"/></exception>
        Task<IResourceLockLease?> TryAcquireAsync(
            ResourceLockMode mode = ResourceLockMode.Exclusive,
            TimeSpan? leaseDuration = null);

        /// <summary>
        /// Try to claim the lock exclusively for an owner, atomically on the server: with
        /// concurrent claims from any process, a single claimer wins. An expired lease is
        /// taken over. The new lease lasts a limited time without renewals: resume the claim
        /// with <see cref="TryResumeClaimAsync"/> to keep it renewed while working, or
        /// release it with <see cref="TryReleaseAsync"/>. The claim and resume split serves
        /// the works claimed by one process and executed by another; a flow acquiring and
        /// working in the same process uses <see cref="TryAcquireAsync"/>.
        /// </summary>
        /// <param name="ownerId">The claiming owner identifier</param>
        /// <param name="leaseDuration">Duration of the claimed lease, defaulted to
        /// <see cref="ResourceLock.DefaultLeaseDuration"/>. It is how long the lock stays
        /// claimed when its owner dies before releasing it, and how long the claim survives
        /// before someone resumes it into a renewed lease: it doesn't have to cover the work
        /// running under the lease, kept renewed in background. The chosen duration is
        /// persisted in the lease document, so any owner resuming the claim renews on it</param>
        /// <returns>True if the lock has been claimed, false if another owner holds it</returns>
        /// <exception cref="ArgumentOutOfRangeException">The lease duration is shorter than
        /// <see cref="ResourceLock.MinLeaseDuration"/></exception>
        Task<bool> TryClaimAsync(string ownerId, TimeSpan? leaseDuration = null);

        /// <summary>
        /// The lease resumed or acquired by an outer section of the current flow, registered
        /// on the execution context.
        /// </summary>
        /// <returns>The ambient lease, or null when the current flow doesn't hold one</returns>
        IResourceLockLease? TryGetAmbientLease();

        /// <summary>
        /// Release a claim never resumed into a lease, permitting new claims without waiting
        /// the lease expiration. The release is owner guarded: a lock already taken over by
        /// another owner stays untouched.
        /// </summary>
        /// <param name="ownerId">The owner identifier used by the claim</param>
        Task TryReleaseAsync(string ownerId);

        /// <summary>
        /// Try to resume a lock claimed with <see cref="TryClaimAsync"/>, verifying the
        /// ownership and renewing the lease. The returned lease keeps renewing in
        /// background, on the duration chosen by its claim, and is registered as ambient on
        /// the execution context, until its disposal releases the lock. Resuming stamps a
        /// fresh lease identifier: a second resume of the same owner invalidates the first
        /// lease, that loses it.
        /// </summary>
        /// <param name="ownerId">The owner identifier used by the claim</param>
        /// <returns>The active lease, or null when the owner doesn't hold the lock anymore</returns>
        Task<IResourceLockLease?> TryResumeClaimAsync(string ownerId);
    }
}
