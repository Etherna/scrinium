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
using Etherna.MongoDB.Bson.Serialization.Serializers;
using Etherna.MongoDB.Driver;
using Etherna.MongoDB.Driver.Core.Clusters;
using Etherna.MongoDB.Driver.Core.Connections;
using Etherna.MongoDB.Driver.Core.Servers;
using Etherna.Scrinium.Core.ExecContext;
using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Etherna.Scrinium.Core.ExecContext.Exceptions;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.Core.Utility
{
    public class ResourceLockTest
    {
        // Consts.
        /* Expirations persist as BSON dates, truncated to the millisecond: one can land just
         * under the instant its computation started from. */
        private static readonly TimeSpan ExpirationTolerance = TimeSpan.FromSeconds(1);
        /* A lease short enough to observe its background renewals inside a test: they run at
         * a fifth of the lease duration carried by the lease document. */
        private static readonly TimeSpan FastRenewalLeaseDuration = TimeSpan.FromMilliseconds(250);
        /* A lease long enough for a renewal to recover from a failed one inside it, on a
         * loaded machine: a recovery landing past the lease duration would lose the lease,
         * which is the behavior of its own test. */
        private static readonly TimeSpan RecoverableRenewalLeaseDuration = TimeSpan.FromSeconds(2.5);
        private const string LockId = "FakeDbContext";
        private static readonly TimeSpan RenewalsWaitBound = TimeSpan.FromSeconds(10);

        // Fields.
        private readonly ResourceLock resourceLock;

        private readonly Mock<IExecutionContext> executionContextMock = new();
        private readonly Mock<IMongoCollection<BsonDocument>> lockCollectionMock = new();

        // Constructor.
        public ResourceLockTest()
        {
            executionContextMock.Setup(c => c.Items).Returns(new Dictionary<object, object?>());

            resourceLock = NewResourceLock(executionContextMock.Object);
        }

        // Tests.
        [Fact]
        public async Task AcquireDeniesOnDuplicateKeyError()
        {
            // Setup.
            //a live lease, exclusive or shared, matches no expiration predicate: the insert collides
            lockCollectionMock.Setup(c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(NewWriteException(ServerErrorCategory.DuplicateKey));

            // Action.
            var lease = await resourceLock.TryAcquireAsync();

            // Assert.
            Assert.Null(lease);
        }

        [Fact]
        public async Task AcquireReleasesTheClaimWhenTheLeaseCantBeBuilt()
        {
            /* The claim already holds the lock: without an owning lease nobody would release
             * it, and it would deny every claimer until its expiration. */

            // Setup.
            //an execution context unable to register the ambient lease
            var brokenExecutionContextMock = new Mock<IExecutionContext>();
            brokenExecutionContextMock.Setup(c => c.Items).Returns((IDictionary<object, object?>?)null);
            var brokenContextLock = NewResourceLock(brokenExecutionContextMock.Object);
            SetupLeaseUpdates();

            // Action and assert.
            await Assert.ThrowsAsync<ExecutionContextNotFoundException>(() =>
                brokenContextLock.TryAcquireAsync());
            lockCollectionMock.Verify(
                c => c.DeleteOneAsync(It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<CancellationToken>()),
                Times.Once());
        }

        [Fact]
        public async Task AcquireStampsAnOwnedLeaseInOneUpsert()
        {
            /* The acquirer is claimer and renewer at once: the lease id stamps with the claim
             * itself, so the acquisition is a single round trip, without the resume step, and
             * the disposal releases exactly the stamped lease. */

            // Setup.
            var writes = SetupUpdateAndFilterCapture();
            List<FilterDefinition<BsonDocument>> releaseFilters = [];
            lockCollectionMock.Setup(c => c.DeleteOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<BsonDocument>, CancellationToken>((filter, _) => releaseFilters.Add(filter))
                .ReturnsAsync(new DeleteResult.Acknowledged(1));
            var chosenLeaseDuration = TimeSpan.FromMinutes(42);
            var acquireStart = DateTime.UtcNow;

            // Action.
            var lease = await resourceLock.TryAcquireAsync(leaseDuration: chosenLeaseDuration);

            // Assert.
            Assert.NotNull(lease);
            Assert.True(writes.TryDequeue(out var acquire));

            //the filter is the exclusive claim one: any live lease denies, an expired one is taken over
            var renderedFilter = Render(acquire.Filter);
            Assert.Equal(LockId, renderedFilter["_id"].AsString);
            Assert.True(renderedFilter.Contains("$or"));

            var claimedElements = Render(acquire.Update)["$set"].AsBsonDocument;
            Assert.Equal(lease.OwnerId, claimedElements["Owner"].AsString);
            var stampedLeaseId = claimedElements["LeaseId"].AsString;
            Assert.Equal(chosenLeaseDuration.Ticks, claimedElements["LeaseDurationTicks"].AsInt64);
            Assert.InRange(
                claimedElements["ExpirationTime"].ToUniversalTime(),
                acquireStart + chosenLeaseDuration - ExpirationTolerance,
                DateTime.UtcNow + chosenLeaseDuration);

            //the disposal releases the acquired lease, guarded by its own identity
            await lease.DisposeAsync();
            var renderedRelease = Render(Assert.Single(releaseFilters));
            Assert.Equal(lease.OwnerId, renderedRelease["Owner"].AsString);
            Assert.Equal(stampedLeaseId, renderedRelease["LeaseId"].AsString);
        }

        [Fact]
        public async Task AmbientLeasesDistinguishNamespacedLockIds()
        {
            /* Application locks identify by their namespaced string id: locks of the same
             * namespace on different resources are different locks, ambient leases included. */

            // Setup.
            SetupLeaseUpdates();
            var pinLock = NewResourceLock(executionContextMock.Object, "pins/A");
            var otherPinLock = NewResourceLock(executionContextMock.Object, "pins/B");

            // Action.
            var lease = await pinLock.TryAcquireAsync();

            // Assert.
            Assert.NotNull(lease);
            Assert.Same(lease, pinLock.TryGetAmbientLease());
            Assert.Null(otherPinLock.TryGetAmbientLease());

            await lease.DisposeAsync();
        }

        [Fact]
        public async Task ClaimDeniesOnDuplicateKeyError()
        {
            // Setup.
            //a live lease matches no expiration predicate: the upsert insert collides on the id index
            lockCollectionMock.Setup(c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(NewWriteException(ServerErrorCategory.DuplicateKey));

            // Action.
            var claimed = await resourceLock.TryClaimAsync("owner");

            // Assert.
            Assert.False(claimed);
        }

        [Fact]
        public async Task ClaimPropagatesWriteErrorsOtherThanDuplicateKey()
        {
            /* Only a duplicate key error means "another owner holds a live lease": reading any
             * other failure as a denial would silently skip seedings and migrations. */

            // Setup.
            lockCollectionMock.Setup(c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(NewWriteException(ServerErrorCategory.Uncategorized));

            // Action and assert.
            await Assert.ThrowsAsync<MongoWriteException>(() => resourceLock.TryClaimAsync("owner"));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        //a lease too short to derive a positive renewal interval from would spin the renewals
        [InlineData(4)]
        public async Task ClaimDeniesUnusableLeaseDurations(long leaseDurationTicks)
        {
            // Action and assert.
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                resourceLock.TryClaimAsync("owner", TimeSpan.FromTicks(leaseDurationTicks)));

            //the denial comes before any write: an unusable lease never reaches the collection
            lockCollectionMock.Verify(
                c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()),
                Times.Never());
        }

        [Fact]
        public async Task ClaimPersistsItsLeaseDuration()
        {
            /* The claimer and the renewer are frequently different processes: the duration
             * chosen by the claim travels in the lease document, or the renewals of another
             * process would have nothing to read it from. */

            // Setup.
            var chosenLeaseDuration = TimeSpan.FromMinutes(42);
            var claimUpdates = SetupUpdateCapture();
            var claimStart = DateTime.UtcNow;

            // Action.
            Assert.True(await resourceLock.TryClaimAsync("owner", chosenLeaseDuration));

            // Assert.
            var claimedElements = Render(Assert.Single(claimUpdates))["$set"].AsBsonDocument;
            Assert.Equal(chosenLeaseDuration.Ticks, claimedElements["LeaseDurationTicks"].AsInt64);
            //and the lease of the claim expires after the same duration
            Assert.InRange(
                claimedElements["ExpirationTime"].ToUniversalTime(),
                claimStart + chosenLeaseDuration - ExpirationTolerance,
                DateTime.UtcNow + chosenLeaseDuration);
        }

        [Fact]
        public async Task ClaimWithoutALeaseDurationPersistsTheDefaultOne()
        {
            // Setup.
            var claimUpdates = SetupUpdateCapture();

            // Action.
            Assert.True(await resourceLock.TryClaimAsync("owner"));

            // Assert.
            //the default duration is persisted like an explicit one: the renewals read it back
            Assert.Equal(
                ResourceLock.DefaultLeaseDuration.Ticks,
                Render(Assert.Single(claimUpdates))["$set"]["LeaseDurationTicks"].AsInt64);
        }

        [Fact]
        public async Task ClaimUpsertsLeaseDocumentOnlyWithoutALiveLease()
        {
            // Setup.
            FilterDefinition<BsonDocument>? claimFilter = null;
            lockCollectionMock.Setup(c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<BsonDocument>, UpdateDefinition<BsonDocument>, UpdateOptions, CancellationToken>(
                    (filter, _, _, _) => claimFilter = filter)
                .ReturnsAsync(new UpdateResult.Acknowledged(0, 0, BsonValue.Create(LockId)));

            // Action.
            var claimed = await resourceLock.TryClaimAsync("owner");

            // Assert.
            //the claim is a single upsert: the atomicity point of the whole lock
            Assert.True(claimed);
            lockCollectionMock.Verify(
                c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    It.Is<UpdateOptions>(o => o.IsUpsert),
                    It.IsAny<CancellationToken>()),
                Times.Once());

            var renderedFilter = Render(claimFilter!);
            //the lease document of this db context, and no other
            Assert.Equal(LockId, renderedFilter["_id"].AsString);
            //an expired lease is taken over: without this predicate a claim would steal a live lease
            var expirationPredicates = renderedFilter["$or"].AsBsonArray;
            Assert.Contains(expirationPredicates, predicate =>
                predicate.ToString()!.Contains("\"$lt\"", StringComparison.Ordinal));
            //a document without a date expiration is claimable too, instead of deadlocking the lock
            Assert.Contains(expirationPredicates, predicate =>
                predicate.ToString()!.Contains("\"$type\"", StringComparison.Ordinal));
        }

        [Fact]
        public async Task ClaimTakesOverDroppingTheSharedLeases()
        {
            // Setup.
            var claimUpdates = SetupUpdateCapture();

            // Action.
            Assert.True(await resourceLock.TryClaimAsync("owner"));

            // Assert.
            //a taken over expired shared document must not keep its dead holder entries
            var unsetElements = Render(Assert.Single(claimUpdates))["$unset"].AsBsonDocument;
            Assert.Contains("SharedLeases", unsetElements.Names);
        }

        [Fact]
        public async Task ClaimsAwaitTheCollectionPreparation()
        {
            /* The preparation ensures the TTL index of the collection before its documents
             * accumulate: every claim or acquisition awaits it first. */

            // Setup.
            var preparations = 0;
            var preparedLock = NewResourceLock(executionContextMock.Object, prepareCollectionAsync: () =>
            {
                preparations++;
                return Task.CompletedTask;
            });
            SetupLeaseUpdates();

            // Action.
            await preparedLock.TryClaimAsync("owner");
            var exclusiveLease = await preparedLock.TryAcquireAsync();
            var sharedLease = await preparedLock.TryAcquireAsync(ResourceLockMode.Shared);

            // Assert.
            Assert.Equal(3, preparations);
            Assert.NotNull(exclusiveLease);
            Assert.NotNull(sharedLease);
            await exclusiveLease.DisposeAsync();
            await sharedLease.DisposeAsync();
        }

        [Fact]
        public async Task DisposalIsIdempotent()
        {
            // Setup.
            SetupLeaseUpdates();
            var lease = await resourceLock.TryResumeClaimAsync("owner");
            Assert.NotNull(lease);

            // Action.
            await lease.DisposeAsync();
            await lease.DisposeAsync();

            // Assert.
            lockCollectionMock.Verify(
                c => c.DeleteOneAsync(It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<CancellationToken>()),
                Times.Once());
        }

        [Fact]
        public async Task DisposalReleasesTheLockAlsoWithAFaultedRenewalTask()
        {
            /* The disposal runs in the finally of the migration execution: a faulted renewal
             * task must neither replace the exception of the migration, nor leave the lock
             * held for the whole lease duration. */

            // Setup.
            //the lease is taken over, and the work registered a throwing lease lost callback
            SetupLeaseStamp(NewLeaseDocument(FastRenewalLeaseDuration));
            lockCollectionMock.SetupSequence(c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null))
                .ReturnsAsync(new UpdateResult.Acknowledged(0, 0, null));

            var lease = await resourceLock.TryResumeClaimAsync("owner");
            Assert.NotNull(lease);

            var leaseLostSignal = new TaskCompletionSource();
            lease.LeaseLostToken.Register(() =>
            {
                leaseLostSignal.TrySetResult();
                throw new InvalidOperationException("Faulting lease lost callback");
            });
            await leaseLostSignal.Task.WaitAsync(RenewalsWaitBound);

            // Action.
            await lease.DisposeAsync();

            // Assert.
            lockCollectionMock.Verify(
                c => c.DeleteOneAsync(It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<CancellationToken>()),
                Times.Once());
        }

        [Fact]
        public async Task IsLockedReportsOnlyALiveLease()
        {
            // Setup.
            FilterDefinition<BsonDocument>? countFilter = null;
            Queue<long> leaseDocumentsCounts = new([1, 0]);
            lockCollectionMock.Setup(c => c.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<BsonDocument>, CountOptions, CancellationToken>(
                    (filter, _, _) => countFilter = filter)
                .ReturnsAsync(leaseDocumentsCounts.Dequeue);

            // Action.
            var isLockedWithLease = await resourceLock.IsLockedAsync();
            var isLockedWithoutLease = await resourceLock.IsLockedAsync();

            // Assert.
            Assert.True(isLockedWithLease);
            Assert.False(isLockedWithoutLease);

            //an expired lease locks nothing: the next claim takes it over
            var renderedFilter = Render(countFilter!);
            Assert.Equal(LockId, renderedFilter["_id"].AsString);
            Assert.True(renderedFilter["ExpirationTime"].AsBsonDocument.Contains("$gt"));
        }

        [Fact]
        public async Task LeaseIsLostWhenRenewalsKeepFailingForTheLeaseDuration()
        {
            /* A lease nobody could renew for its whole duration can already have been taken
             * over: the work under it must abort, without waiting to observe the takeover. */

            // Setup.
            //a lease claimed for a short duration: its renewals run, and fail, at a fifth of it
            SetupLeaseStamp(NewLeaseDocument(FastRenewalLeaseDuration));

            var updates = 0;
            lockCollectionMock.Setup(c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() => Interlocked.Increment(ref updates) == 1
                    ? Task.FromResult<UpdateResult>(new UpdateResult.Acknowledged(1, 1, null))
                    : Task.FromException<UpdateResult>(new MongoConnectionException(
                        NewConnectionId(), "The server is unreachable")));

            var lease = await resourceLock.TryResumeClaimAsync("owner");
            Assert.NotNull(lease);

            // Action.
            var leaseLostSignal = new TaskCompletionSource();
            lease.LeaseLostToken.Register(() => leaseLostSignal.TrySetResult());

            // Assert.
            await leaseLostSignal.Task.WaitAsync(RenewalsWaitBound);
            Assert.True(lease.LeaseLostToken.IsCancellationRequested);
            await lease.DisposeAsync();
        }

        [Fact]
        public async Task LeaseLostTokenFiresWhenRenewalIsDenied()
        {
            // Setup.
            //the resume renewal succeeds, then the lease is taken over by another claimer
            SetupLeaseStamp(NewLeaseDocument(FastRenewalLeaseDuration));
            lockCollectionMock.SetupSequence(c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null))
                .ReturnsAsync(new UpdateResult.Acknowledged(0, 0, null));

            var lease = await resourceLock.TryResumeClaimAsync("owner");
            Assert.NotNull(lease);

            // Action.
            var leaseLostSignal = new TaskCompletionSource();
            lease.LeaseLostToken.Register(() => leaseLostSignal.SetResult());
            await leaseLostSignal.Task.WaitAsync(RenewalsWaitBound);

            // Assert.
            Assert.True(lease.LeaseLostToken.IsCancellationRequested);
            await lease.DisposeAsync();
        }

        [Fact]
        public async Task LeaseSurvivesATransientRenewalFailure()
        {
            /* A renewal failing inside the lease duration doesn't end the lease: the lease is
             * still alive until its expiration, and the next renewals keep it so. */

            // Setup.
            //renewals run at a fifth of the lease: the recovery lands well inside it
            SetupLeaseStamp(NewLeaseDocument(RecoverableRenewalLeaseDuration));

            var renewalsSignal = new TaskCompletionSource();
            var updates = 0;
            lockCollectionMock.Setup(c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    //the first renewal after the resume fails, the following ones recover
                    var update = Interlocked.Increment(ref updates);
                    if (update == 2)
                        return Task.FromException<UpdateResult>(new MongoConnectionException(
                            NewConnectionId(), "The server is unreachable"));

                    //the resume renewed, the second failed, this one recovers
                    if (update >= 3)
                        renewalsSignal.TrySetResult();
                    return Task.FromResult<UpdateResult>(new UpdateResult.Acknowledged(1, 1, null));
                });

            var lease = await resourceLock.TryResumeClaimAsync("owner");
            Assert.NotNull(lease);

            // Action.
            await renewalsSignal.Task.WaitAsync(RenewalsWaitBound);

            // Assert.
            //the renewals recovered from the failure, keeping the lease of the running work
            Assert.False(lease.LeaseLostToken.IsCancellationRequested);
            await lease.DisposeAsync();
        }

        [Fact]
        public async Task ResumeCreatesAnExecutionContextWhenTheFlowHasNone()
        {
            /* A migration can execute outside any ambient context: a console application, a
             * hosted service, a custom task runner, or the dry run branch of the migration
             * task, that doesn't enter an exclusive access. */

            // Setup.
            var contextLessLock = NewResourceLock(AsyncLocalContext.Instance);
            SetupLeaseUpdates();
            Assert.Null(AsyncLocalContext.Instance.Items);

            // Action.
            var lease = await contextLessLock.TryResumeClaimAsync("owner");

            // Assert.
            Assert.NotNull(lease);
            await lease.DisposeAsync();
        }

        [Fact]
        public async Task ResumeDeniesWhenOwnerDoesNotHoldLock()
        {
            // Setup.
            //no lease document of this owner to stamp: the lock has been taken over, or released
            SetupLeaseStamp(leaseDocument: null);

            // Action.
            var lease = await resourceLock.TryResumeClaimAsync("owner");

            // Assert.
            Assert.Null(lease);
            Assert.Null(resourceLock.TryGetAmbientLease());
        }

        [Fact]
        public async Task ResumeDeniesWhenTheLockIsTakenOverWhileStamping()
        {
            /* The stamp reads the lease duration back, and the first renewal extends the lease
             * on it: a takeover landing in between leaves the resumed owner without a lease. */

            // Setup.
            SetupLeaseStamp();
            lockCollectionMock.Setup(c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UpdateResult.Acknowledged(0, 0, null));

            // Action.
            var lease = await resourceLock.TryResumeClaimAsync("owner");

            // Assert.
            Assert.Null(lease);
            Assert.Null(resourceLock.TryGetAmbientLease());
        }

        [Fact]
        public async Task ResumeFallsBackToTheDefaultLeaseDurationWithoutAPersistedOne()
        {
            /* A lease document carrying no duration must stay resumable: renewing it on the
             * default keeps the lock working, instead of leaving a claim nobody can resume. */

            // Setup.
            //a lease document claimed by an instance that doesn't persist the duration
            SetupLeaseStamp(NewLeaseDocument(leaseDuration: null));
            var renewals = SetupUpdateCapture();
            var resumeStart = DateTime.UtcNow;

            // Action.
            var lease = await resourceLock.TryResumeClaimAsync("owner");

            // Assert.
            Assert.NotNull(lease);
            //the renewal of the resume extends the lease by the default duration
            Assert.InRange(
                Render(Assert.Single(renewals))["$set"]["ExpirationTime"].ToUniversalTime(),
                resumeStart + ResourceLock.DefaultLeaseDuration - ExpirationTolerance,
                DateTime.UtcNow + ResourceLock.DefaultLeaseDuration);

            await lease.DisposeAsync();
        }

        [Fact]
        public async Task ResumeFromAnotherLockInstanceRenewsOnThePersistedLeaseDuration()
        {
            /* The claimer and the renewer are frequently different processes: a dashboard web
             * process claims the lock starting a migration, and the background worker resumes
             * it. The renewals of the worker must run on the duration chosen by the claim,
             * that only the lease document carries. */

            // Setup.
            //the lock instance of the other process: it claimed nothing, it only resumes
            var workerResourceLock = NewResourceLock(executionContextMock.Object);
            SetupLeaseStamp(NewLeaseDocument(FastRenewalLeaseDuration));
            var renewals = SetupUpdateCapture();
            var resumeStart = DateTime.UtcNow;

            // Action.
            var lease = await workerResourceLock.TryResumeClaimAsync("owner");
            Assert.NotNull(lease);
            //the renewals run at a fifth of the persisted duration: the default one would be far slower
            await WaitForUpdatesAsync(renewals, 3);
            await lease.DisposeAsync();

            // Assert.
            //every renewal extends the lease by the persisted duration, not by the default one
            Assert.All(renewals, renewal => Assert.InRange(
                Render(renewal)["$set"]["ExpirationTime"].ToUniversalTime(),
                resumeStart,
                DateTime.UtcNow + FastRenewalLeaseDuration));
        }

        [Fact]
        public async Task ResumeFencesEachLeaseWithItsOwnLeaseId()
        {
            /* The owner id alone can't fence two executions of the same operation (a task
             * runner delivering it twice): the loser would release the lease of the winner,
             * freeing the lock for a third instance while the winner is still migrating. */

            // Setup.
            SetupLeaseUpdates();
            List<FilterDefinition<BsonDocument>> releaseFilters = [];
            lockCollectionMock.Setup(c => c.DeleteOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<BsonDocument>, CancellationToken>((filter, _) => releaseFilters.Add(filter))
                .ReturnsAsync(new DeleteResult.Acknowledged(0));

            // Action.
            var firstLease = await resourceLock.TryResumeClaimAsync("owner");
            var secondLease = await resourceLock.TryResumeClaimAsync("owner");
            Assert.NotNull(firstLease);
            Assert.NotNull(secondLease);
            await firstLease.DisposeAsync();
            await secondLease.DisposeAsync();

            // Assert.
            //each release is guarded by its own lease id: the invalidated lease deletes nothing
            var releasedLeaseIds = releaseFilters
                .Select(filter => Render(filter)["LeaseId"].AsString)
                .ToArray();
            Assert.Equal(2, releasedLeaseIds.Length);
            Assert.NotEqual(releasedLeaseIds[0], releasedLeaseIds[1]);
        }

        [Fact]
        public async Task ResumeRegistersAmbientLeaseUntilDisposalReleases()
        {
            // Setup.
            SetupLeaseUpdates();

            // Action.
            var lease = await resourceLock.TryResumeClaimAsync("owner");

            // Assert.
            //the lease is the ambient one of the flow, until its disposal releases the lock
            Assert.NotNull(lease);
            Assert.Equal("owner", lease.OwnerId);
            Assert.Same(lease, resourceLock.TryGetAmbientLease());

            await lease.DisposeAsync();
            Assert.Null(resourceLock.TryGetAmbientLease());
            lockCollectionMock.Verify(
                c => c.DeleteOneAsync(It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<CancellationToken>()),
                Times.Once());
        }

        [Fact]
        public async Task ResumeReleasesTheStampedLeaseWhenTheLeaseCantBeBuilt()
        {
            /* The stamp already pushed the expiration forward: without an owning lease
             * nobody would release the lock, held until the expiration by nothing. */

            // Setup.
            //an execution context unable to register the ambient lease
            var brokenExecutionContextMock = new Mock<IExecutionContext>();
            brokenExecutionContextMock.Setup(c => c.Items).Returns((IDictionary<object, object?>?)null);
            var brokenContextLock = NewResourceLock(brokenExecutionContextMock.Object);
            SetupLeaseUpdates();

            // Action and assert.
            await Assert.ThrowsAsync<ExecutionContextNotFoundException>(() =>
                brokenContextLock.TryResumeClaimAsync("owner"));
            lockCollectionMock.Verify(
                c => c.DeleteOneAsync(It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<CancellationToken>()),
                Times.Once());
        }

        [Fact]
        public async Task ResumeSupportsNestedLeases()
        {
            // Setup.
            SetupLeaseUpdates();

            // Action.
            var outerLease = await resourceLock.TryResumeClaimAsync("owner");
            var innerLease = await resourceLock.TryResumeClaimAsync("owner");

            // Assert.
            //the innermost lease of the flow is the ambient one, and the outer one resumes after it
            Assert.Same(innerLease, resourceLock.TryGetAmbientLease());
            await innerLease!.DisposeAsync();
            Assert.Same(outerLease, resourceLock.TryGetAmbientLease());
            await outerLease!.DisposeAsync();
            Assert.Null(resourceLock.TryGetAmbientLease());
        }

        [Fact]
        public async Task SharedAcquireAppendsItsLeaseDroppingTheExpiredEntries()
        {
            // Setup.
            var writes = SetupUpdateAndFilterCapture();
            var chosenLeaseDuration = TimeSpan.FromMinutes(42);
            var acquireStart = DateTime.UtcNow;

            // Action.
            var lease = await resourceLock.TryAcquireAsync(ResourceLockMode.Shared, chosenLeaseDuration);

            // Assert.
            Assert.NotNull(lease);
            Assert.True(writes.TryDequeue(out var acquire));

            //only a live exclusive lease denies: any shared document, and any expired one, admits
            var renderedFilter = Render(acquire.Filter);
            Assert.Equal(LockId, renderedFilter["_id"].AsString);
            var admissionPredicates = renderedFilter["$or"].AsBsonArray;
            Assert.Contains(admissionPredicates, predicate =>
                predicate.ToString()!.Contains("\"$exists\"", StringComparison.Ordinal));
            Assert.Contains(admissionPredicates, predicate =>
                predicate.ToString()!.Contains("\"$lt\"", StringComparison.Ordinal));
            Assert.Contains(admissionPredicates, predicate =>
                predicate.ToString()!.Contains("\"$type\"", StringComparison.Ordinal));

            /* The pipeline appends the own lease dropping the expired entries in the same
             * write, keeps the top level expiration on the furthest lease, and clears the
             * ownership elements of a taken over exclusive lease. */
            var acquireStages = RenderPipeline(acquire.Update);
            var appendedLeases = acquireStages[0]["$set"]["SharedLeases"]["$concatArrays"].AsBsonArray;
            Assert.Contains("$filter", appendedLeases[0].AsBsonDocument.Names);
            var appendedLease = Assert.Single(appendedLeases[1].AsBsonArray).AsBsonDocument;
            Assert.Equal(lease.OwnerId, appendedLease["Owner"].AsString);
            Assert.InRange(
                appendedLease["ExpirationTime"].ToUniversalTime(),
                acquireStart + chosenLeaseDuration - ExpirationTolerance,
                DateTime.UtcNow + chosenLeaseDuration);
            Assert.Equal(
                "$SharedLeases.ExpirationTime",
                acquireStages[1]["$set"]["ExpirationTime"]["$max"].AsString);
            Assert.Contains("Owner", acquireStages[2]["$unset"].AsBsonArray.Select(name => name.AsString));

            await lease.DisposeAsync();
        }

        [Fact]
        public async Task SharedAcquireDeniesWhenACollisionShowsALiveExclusiveLease()
        {
            // Setup.
            lockCollectionMock.Setup(c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(NewWriteException(ServerErrorCategory.DuplicateKey));

            FilterDefinition<BsonDocument>? exclusiveLeaseFilter = null;
            lockCollectionMock.Setup(c => c.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<BsonDocument>, CountOptions, CancellationToken>(
                    (filter, _, _) => exclusiveLeaseFilter = filter)
                .ReturnsAsync(1);

            // Action.
            var lease = await resourceLock.TryAcquireAsync(ResourceLockMode.Shared);

            // Assert.
            //the read told a live exclusive lease: a truthful denial, without further attempts
            Assert.Null(lease);
            lockCollectionMock.Verify(
                c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()),
                Times.Once());

            //the read looks for a live lease with an owner: the exclusive shape
            var renderedFilter = Render(exclusiveLeaseFilter!);
            Assert.Equal(LockId, renderedFilter["_id"].AsString);
            Assert.True(renderedFilter["Owner"].AsBsonDocument.Contains("$exists"));
            Assert.True(renderedFilter["ExpirationTime"].AsBsonDocument.Contains("$gt"));
        }

        [Fact]
        public async Task SharedAcquireGivesUpAfterRepeatedCollisions()
        {
            /* Collisions without a live exclusive lease are concurrent claims racing on the
             * insert: the retries are bounded, and an exhausted acquisition reports denied. */

            // Setup.
            lockCollectionMock.Setup(c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(NewWriteException(ServerErrorCategory.DuplicateKey));
            lockCollectionMock.Setup(c => c.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);

            // Action.
            var lease = await resourceLock.TryAcquireAsync(ResourceLockMode.Shared);

            // Assert.
            Assert.Null(lease);
            //three bounded attempts, and no read after the last one: it couldn't admit a retry
            lockCollectionMock.Verify(
                c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(3));
            lockCollectionMock.Verify(
                c => c.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }

        [Fact]
        public async Task SharedAcquireRetriesWhenTheCollisionIsAConcurrentSharedClaim()
        {
            /* Two shared acquisitions inserting the first lease of a resource collide: the
             * loser must retry and append to the document existing now, not report denied. */

            // Setup.
            lockCollectionMock.SetupSequence(c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(NewWriteException(ServerErrorCategory.DuplicateKey))
                .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));
            lockCollectionMock.Setup(c => c.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);

            // Action.
            var lease = await resourceLock.TryAcquireAsync(ResourceLockMode.Shared);

            // Assert.
            Assert.NotNull(lease);
            lockCollectionMock.Verify(
                c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));

            await lease.DisposeAsync();
        }

        [Fact]
        public async Task SharedAcquireReleasesItsLeaseWhenTheLeaseCantBeBuilt()
        {
            /* The claim already appended its lease entry: without an owning lease nobody
             * would release it, and it would deny the exclusive claimers until expiration. */

            // Setup.
            //an execution context unable to register the ambient lease
            var brokenExecutionContextMock = new Mock<IExecutionContext>();
            brokenExecutionContextMock.Setup(c => c.Items).Returns((IDictionary<object, object?>?)null);
            var brokenContextLock = NewResourceLock(brokenExecutionContextMock.Object);
            SetupUpdateCapture();
            var releases = SetupSharedReleaseCapture(releasedDocument: null);

            // Action and assert.
            await Assert.ThrowsAsync<ExecutionContextNotFoundException>(() =>
                brokenContextLock.TryAcquireAsync(ResourceLockMode.Shared));
            Assert.Single(releases);
        }

        [Fact]
        public async Task SharedLeaseDisposalDeletesTheEmptiedLockDocument()
        {
            /* The last holder leaving must open the lock to exclusive claims right away, and
             * not leave a document behind for every released resource. */

            // Setup.
            var writes = SetupUpdateAndFilterCapture();
            var releases = SetupSharedReleaseCapture(new BsonDocument
            {
                ["_id"] = LockId,
                ["ExpirationTime"] = DateTime.UtcNow,
                ["SharedLeases"] = new BsonArray()
            });
            List<FilterDefinition<BsonDocument>> deleteFilters = [];
            lockCollectionMock.Setup(c => c.DeleteOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<BsonDocument>, CancellationToken>((filter, _) => deleteFilters.Add(filter))
                .ReturnsAsync(new DeleteResult.Acknowledged(1));

            var lease = await resourceLock.TryAcquireAsync(ResourceLockMode.Shared);
            Assert.NotNull(lease);
            Assert.True(writes.TryDequeue(out var acquire));
            var acquiredLeaseId = RenderPipeline(acquire.Update)[0]["$set"]["SharedLeases"]["$concatArrays"]
                .AsBsonArray[1].AsBsonArray[0]["LeaseId"].AsString;

            // Action.
            await lease.DisposeAsync();

            // Assert.
            //the release addresses the own lease entry only, and drops the expired ones with it
            var (releaseFilter, releaseUpdate) = Assert.Single(releases);
            var renderedReleaseFilter = Render(releaseFilter);
            Assert.Equal(LockId, renderedReleaseFilter["_id"].AsString);
            Assert.Equal(
                acquiredLeaseId,
                renderedReleaseFilter["SharedLeases"]["$elemMatch"]["LeaseId"].AsString);
            var releaseStages = RenderPipeline(releaseUpdate);
            var retainedLeasesConditions = releaseStages[0]["$set"]["SharedLeases"]["$filter"]["cond"]["$and"].AsBsonArray;
            Assert.Contains(retainedLeasesConditions, condition =>
                condition.ToString()!.Contains("\"$ne\"", StringComparison.Ordinal));
            Assert.Contains(retainedLeasesConditions, condition =>
                condition.ToString()!.Contains("$$NOW", StringComparison.Ordinal));
            //the expiration recomputes on the remaining leases, closing an emptied lock
            Assert.True(releaseStages[1]["$set"]["ExpirationTime"].AsBsonDocument.Contains("$cond"));

            //the emptied document deletes, guarded against concurrent claims landing in between
            var renderedDeleteFilter = Render(Assert.Single(deleteFilters));
            Assert.Equal(LockId, renderedDeleteFilter["_id"].AsString);
            Assert.Equal(0, renderedDeleteFilter["SharedLeases"]["$size"].AsInt32);
            Assert.False(renderedDeleteFilter["Owner"]["$exists"].AsBoolean);
        }

        [Fact]
        public async Task SharedLeaseDisposalKeepsTheDocumentOfTheRemainingHolders()
        {
            // Setup.
            SetupUpdateCapture();
            SetupSharedReleaseCapture(new BsonDocument
            {
                ["_id"] = LockId,
                ["ExpirationTime"] = DateTime.UtcNow + TimeSpan.FromMinutes(10),
                ["SharedLeases"] = new BsonArray
                {
                    new BsonDocument
                    {
                        ["ExpirationTime"] = DateTime.UtcNow + TimeSpan.FromMinutes(10),
                        ["LeaseId"] = "remaining-holder-lease",
                        ["Owner"] = "remaining-holder"
                    }
                }
            });
            var lease = await resourceLock.TryAcquireAsync(ResourceLockMode.Shared);
            Assert.NotNull(lease);

            // Action.
            await lease.DisposeAsync();

            // Assert.
            //the resource is still locked by the other holders: nothing to delete
            lockCollectionMock.Verify(
                c => c.DeleteOneAsync(It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<CancellationToken>()),
                Times.Never());
        }

        [Fact]
        public async Task SharedLeaseIsLostWhenItsEntryDisappears()
        {
            /* A fully expired shared document is taken over, and its entries dropped: the
             * renewal matching nothing must abort the work believing it holds the lock. */

            // Setup.
            var updates = 0;
            lockCollectionMock.Setup(c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() => Interlocked.Increment(ref updates) == 1
                    ? Task.FromResult<UpdateResult>(new UpdateResult.Acknowledged(1, 1, null))
                    : Task.FromResult<UpdateResult>(new UpdateResult.Acknowledged(0, 0, null)));

            var lease = await resourceLock.TryAcquireAsync(ResourceLockMode.Shared, FastRenewalLeaseDuration);
            Assert.NotNull(lease);

            // Action.
            var leaseLostSignal = new TaskCompletionSource();
            lease.LeaseLostToken.Register(() => leaseLostSignal.TrySetResult());
            await leaseLostSignal.Task.WaitAsync(RenewalsWaitBound);

            // Assert.
            Assert.True(lease.LeaseLostToken.IsCancellationRequested);
            await lease.DisposeAsync();
        }

        [Fact]
        public async Task SharedLeaseRenewsOnlyItsOwnEntry()
        {
            /* Every shared holder renews its own lease entry, fenced by its lease id, and
             * keeps the top level expiration tracking the furthest lease. */

            // Setup.
            var writes = SetupUpdateAndFilterCapture();

            // Action.
            var lease = await resourceLock.TryAcquireAsync(ResourceLockMode.Shared, FastRenewalLeaseDuration);
            Assert.NotNull(lease);
            await WaitForUpdatesAsync(writes, 3);
            await lease.DisposeAsync();

            // Assert.
            Assert.True(writes.TryDequeue(out var acquire));
            var acquiredLeaseId = RenderPipeline(acquire.Update)[0]["$set"]["SharedLeases"]["$concatArrays"]
                .AsBsonArray[1].AsBsonArray[0]["LeaseId"].AsString;

            Assert.True(writes.TryDequeue(out var renewal));
            var renderedRenewalFilter = Render(renewal.Filter);
            Assert.Equal(LockId, renderedRenewalFilter["_id"].AsString);
            Assert.Equal(
                acquiredLeaseId,
                renderedRenewalFilter["SharedLeases"]["$elemMatch"]["LeaseId"].AsString);

            var renderedRenewal = Render(renewal.Update);
            Assert.True(renderedRenewal["$set"].AsBsonDocument.Contains("SharedLeases.$.ExpirationTime"));
            Assert.True(renderedRenewal["$max"].AsBsonDocument.Contains("ExpirationTime"));
        }

        [Fact]
        public async Task TryGetAmbientLeaseIgnoresTheLeasesOfOtherLocks()
        {
            /* The ambient leases of a flow share one execution context slot: resolving the
             * lease of another lock would run an exclusive work of this db context believing
             * it holds a lock claimed for another one. */

            // Setup.
            SetupLeaseUpdates();
            var otherResourceLock = NewResourceLock(executionContextMock.Object, "OtherFakeDbContext");

            // Action.
            var otherLease = await otherResourceLock.TryResumeClaimAsync("owner");

            // Assert.
            Assert.NotNull(otherLease);
            Assert.Same(otherLease, otherResourceLock.TryGetAmbientLease());
            Assert.Null(resourceLock.TryGetAmbientLease());

            await otherLease.DisposeAsync();
        }

        [Fact]
        public void TryGetAmbientLeaseReturnsNullWithoutExecutionContext()
        {
            // Setup.
            var contextLessLockMock = new Mock<IExecutionContext>();
            contextLessLockMock.Setup(c => c.Items).Returns((IDictionary<object, object?>?)null);

            // Action and assert.
            //a flow without ambient state simply holds no lease
            Assert.Null(NewResourceLock(contextLessLockMock.Object).TryGetAmbientLease());
        }

        [Fact]
        public async Task TryReleaseDeletesTheClaimOfItsOwnerOnly()
        {
            // Setup.
            FilterDefinition<BsonDocument>? releaseFilter = null;
            lockCollectionMock.Setup(c => c.DeleteOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<BsonDocument>, CancellationToken>((filter, _) => releaseFilter = filter)
                .ReturnsAsync(new DeleteResult.Acknowledged(1));

            // Action.
            await resourceLock.TryReleaseAsync("owner");

            // Assert.
            //a claim never resumed has no lease id yet: the owner guards it alone
            var renderedFilter = Render(releaseFilter!);
            Assert.Equal(LockId, renderedFilter["_id"].AsString);
            Assert.Equal("owner", renderedFilter["Owner"].AsString);
            Assert.False(renderedFilter.Contains("LeaseId"));
        }

        // Helpers.
        private static ConnectionId NewConnectionId() =>
            new(new ServerId(new ClusterId(0), new DnsEndPoint("localhost", 27017)));

        private ResourceLock NewResourceLock(
            IExecutionContext executionContext,
            string? lockId = null,
            Func<Task>? prepareCollectionAsync = null) =>
            new(lockCollectionMock.Object,
                lockId ?? LockId,
                executionContext,
                new Mock<ILogger>().Object,
                prepareCollectionAsync);

        private static BsonDocument NewLeaseDocument(TimeSpan? leaseDuration)
        {
            var leaseDocument = new BsonDocument
            {
                ["_id"] = LockId,
                ["Owner"] = "owner"
            };

            if (leaseDuration is not null)
                leaseDocument["LeaseDurationTicks"] = leaseDuration.Value.Ticks;

            return leaseDocument;
        }

        /* The driver keeps the write error constructor internal: build a real write exception
         * of the wanted category, instead of asserting on a shape the lock never receives. */
        private static MongoWriteException NewWriteException(ServerErrorCategory category) =>
            new(NewConnectionId(),
                (WriteError)Activator.CreateInstance(
                    typeof(WriteError),
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    [category, 11000, "Write error", null],
                    CultureInfo.InvariantCulture)!,
                null,
                null);

        private static BsonDocument Render(FilterDefinition<BsonDocument> filter) =>
            filter.Render(new RenderArgs<BsonDocument>(
                BsonDocumentSerializer.Instance,
                BsonSerializer.GetSerializerRegistry(true)));

        private static BsonDocument Render(UpdateDefinition<BsonDocument> update) =>
            update.Render(new RenderArgs<BsonDocument>(
                BsonDocumentSerializer.Instance,
                BsonSerializer.GetSerializerRegistry(true))).AsBsonDocument;

        //a pipeline update renders as its stages array, not as an update document
        private static BsonArray RenderPipeline(UpdateDefinition<BsonDocument> update) =>
            update.Render(new RenderArgs<BsonDocument>(
                BsonDocumentSerializer.Instance,
                BsonSerializer.GetSerializerRegistry(true))).AsBsonArray;

        /* The resume stamps a fresh lease id on the lock document, reading it back to renew
         * on the lease duration its claim persisted. */
        private void SetupLeaseStamp(BsonDocument? leaseDocument) =>
            lockCollectionMock.Setup(c => c.FindOneAndUpdateAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    It.IsAny<FindOneAndUpdateOptions<BsonDocument, BsonDocument>>(),
                    It.IsAny<CancellationToken>()))
                //the driver returns no document when the filter matches nothing
                .ReturnsAsync(leaseDocument!);

        private void SetupLeaseStamp() =>
            SetupLeaseStamp(NewLeaseDocument(ResourceLock.DefaultLeaseDuration));

        private void SetupLeaseUpdates()
        {
            SetupLeaseStamp();
            lockCollectionMock.Setup(c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));
        }

        /* The shared release pulls the own lease entry with a find and update, deciding the
         * delete of an emptied document on the returned state. */
        private List<(FilterDefinition<BsonDocument> Filter, UpdateDefinition<BsonDocument> Update)> SetupSharedReleaseCapture(
            BsonDocument? releasedDocument)
        {
            List<(FilterDefinition<BsonDocument> Filter, UpdateDefinition<BsonDocument> Update)> releases = [];
            lockCollectionMock.Setup(c => c.FindOneAndUpdateAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    It.IsAny<FindOneAndUpdateOptions<BsonDocument, BsonDocument>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<BsonDocument>, UpdateDefinition<BsonDocument>, FindOneAndUpdateOptions<BsonDocument, BsonDocument>, CancellationToken>(
                    (filter, update, _, _) => releases.Add((filter, update)))
                .ReturnsAsync(releasedDocument!);
            return releases;
        }

        /* Collect the writes on the lock collection with their filters: claims, acquisitions,
         * and the lease renewals running in background. */
        private ConcurrentQueue<(FilterDefinition<BsonDocument> Filter, UpdateDefinition<BsonDocument> Update)> SetupUpdateAndFilterCapture()
        {
            ConcurrentQueue<(FilterDefinition<BsonDocument> Filter, UpdateDefinition<BsonDocument> Update)> writes = [];
            lockCollectionMock.Setup(c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<BsonDocument>, UpdateDefinition<BsonDocument>, UpdateOptions, CancellationToken>(
                    (filter, update, _, _) => writes.Enqueue((filter, update)))
                .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));
            return writes;
        }

        /* Collect the updates written on the lock collection: claims, and the lease renewals
         * running in background. */
        private ConcurrentQueue<UpdateDefinition<BsonDocument>> SetupUpdateCapture()
        {
            ConcurrentQueue<UpdateDefinition<BsonDocument>> updates = [];
            lockCollectionMock.Setup(c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<BsonDocument>, UpdateDefinition<BsonDocument>, UpdateOptions, CancellationToken>(
                    (_, update, _, _) => updates.Enqueue(update))
                .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));
            return updates;
        }

        private static async Task WaitForUpdatesAsync<T>(
            ConcurrentQueue<T> updates,
            int expectedCount)
        {
            var waitStart = DateTime.UtcNow;
            while (updates.Count < expectedCount)
            {
                Assert.True(
                    DateTime.UtcNow - waitStart < RenewalsWaitBound,
                    $"Only {updates.Count} of the {expectedCount} expected lease renewals ran");
                await Task.Delay(10);
            }
        }
    }
}
