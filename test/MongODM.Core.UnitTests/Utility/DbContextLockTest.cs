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
using Etherna.MongODM.Core.ExecContext;
using Etherna.MongODM.Core.ExecContext.AsyncLocal;
using Etherna.MongODM.Core.ExecContext.Exceptions;
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

namespace Etherna.MongODM.Core.Utility
{
    public class DbContextLockTest
    {
        // Consts.
        /* Expirations persist as BSON dates, truncated to the millisecond: one can land just
         * under the instant its computation started from. */
        private static readonly TimeSpan ExpirationTolerance = TimeSpan.FromSeconds(1);
        /* A lease short enough to observe its background renewals inside a test: they run at
         * a fifth of the lease duration carried by the lease document. */
        private static readonly TimeSpan FastRenewalLeaseDuration = TimeSpan.FromMilliseconds(250);
        private const string LockId = "FakeDbContext";
        private static readonly TimeSpan RenewalsWaitBound = TimeSpan.FromSeconds(10);

        // Fields.
        private readonly DbContextLock dbContextLock;

        private readonly Mock<IExecutionContext> executionContextMock = new();
        private readonly Mock<IMongoCollection<BsonDocument>> lockCollectionMock = new();

        // Constructor.
        public DbContextLockTest()
        {
            executionContextMock.Setup(c => c.Items).Returns(new Dictionary<object, object?>());

            dbContextLock = NewDbContextLock(executionContextMock.Object);
        }

        // Tests.
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
            var claimed = await dbContextLock.TryClaimAsync("owner");

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
            await Assert.ThrowsAsync<MongoWriteException>(() => dbContextLock.TryClaimAsync("owner"));
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
                dbContextLock.TryClaimAsync("owner", TimeSpan.FromTicks(leaseDurationTicks)));

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
            Assert.True(await dbContextLock.TryClaimAsync("owner", chosenLeaseDuration));

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
            Assert.True(await dbContextLock.TryClaimAsync("owner"));

            // Assert.
            //the default duration is persisted like an explicit one: the renewals read it back
            Assert.Equal(
                DbContextLock.DefaultLeaseDuration.Ticks,
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
            var claimed = await dbContextLock.TryClaimAsync("owner");

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
        public async Task DisposalIsIdempotent()
        {
            // Setup.
            SetupLeaseUpdates();
            var lease = await dbContextLock.TryResumeClaimAsync("owner");
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

            var lease = await dbContextLock.TryResumeClaimAsync("owner");
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
            var isLockedWithLease = await dbContextLock.IsLockedAsync();
            var isLockedWithoutLease = await dbContextLock.IsLockedAsync();

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

            var lease = await dbContextLock.TryResumeClaimAsync("owner");
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

            var lease = await dbContextLock.TryResumeClaimAsync("owner");
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
            //a lease claimed for a short duration: its renewals run at a fifth of it
            SetupLeaseStamp(NewLeaseDocument(FastRenewalLeaseDuration));

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

                    if (update >= 4)
                        renewalsSignal.TrySetResult();
                    return Task.FromResult<UpdateResult>(new UpdateResult.Acknowledged(1, 1, null));
                });

            var lease = await dbContextLock.TryResumeClaimAsync("owner");
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
            var contextLessLock = NewDbContextLock(AsyncLocalContext.Instance);
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
            var lease = await dbContextLock.TryResumeClaimAsync("owner");

            // Assert.
            Assert.Null(lease);
            Assert.Null(dbContextLock.TryGetAmbientLease());
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
            var lease = await dbContextLock.TryResumeClaimAsync("owner");

            // Assert.
            Assert.Null(lease);
            Assert.Null(dbContextLock.TryGetAmbientLease());
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
            var lease = await dbContextLock.TryResumeClaimAsync("owner");

            // Assert.
            Assert.NotNull(lease);
            //the renewal of the resume extends the lease by the default duration
            Assert.InRange(
                Render(Assert.Single(renewals))["$set"]["ExpirationTime"].ToUniversalTime(),
                resumeStart + DbContextLock.DefaultLeaseDuration - ExpirationTolerance,
                DateTime.UtcNow + DbContextLock.DefaultLeaseDuration);

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
            var workerDbContextLock = NewDbContextLock(executionContextMock.Object);
            SetupLeaseStamp(NewLeaseDocument(FastRenewalLeaseDuration));
            var renewals = SetupUpdateCapture();
            var resumeStart = DateTime.UtcNow;

            // Action.
            var lease = await workerDbContextLock.TryResumeClaimAsync("owner");
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
            var firstLease = await dbContextLock.TryResumeClaimAsync("owner");
            var secondLease = await dbContextLock.TryResumeClaimAsync("owner");
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
            var lease = await dbContextLock.TryResumeClaimAsync("owner");

            // Assert.
            //the lease is the ambient one of the flow, until its disposal releases the lock
            Assert.NotNull(lease);
            Assert.Equal("owner", lease.OwnerId);
            Assert.Same(lease, dbContextLock.TryGetAmbientLease());

            await lease.DisposeAsync();
            Assert.Null(dbContextLock.TryGetAmbientLease());
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
            var brokenContextLock = NewDbContextLock(brokenExecutionContextMock.Object);
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
            var outerLease = await dbContextLock.TryResumeClaimAsync("owner");
            var innerLease = await dbContextLock.TryResumeClaimAsync("owner");

            // Assert.
            //the innermost lease of the flow is the ambient one, and the outer one resumes after it
            Assert.Same(innerLease, dbContextLock.TryGetAmbientLease());
            await innerLease!.DisposeAsync();
            Assert.Same(outerLease, dbContextLock.TryGetAmbientLease());
            await outerLease!.DisposeAsync();
            Assert.Null(dbContextLock.TryGetAmbientLease());
        }

        [Fact]
        public async Task TryGetAmbientLeaseIgnoresTheLeasesOfOtherLocks()
        {
            /* The ambient leases of a flow share one execution context slot: resolving the
             * lease of another lock would run an exclusive work of this db context believing
             * it holds a lock claimed for another one. */

            // Setup.
            SetupLeaseUpdates();
            var otherDbContextLock = NewDbContextLock(executionContextMock.Object, "OtherFakeDbContext");

            // Action.
            var otherLease = await otherDbContextLock.TryResumeClaimAsync("owner");

            // Assert.
            Assert.NotNull(otherLease);
            Assert.Same(otherLease, otherDbContextLock.TryGetAmbientLease());
            Assert.Null(dbContextLock.TryGetAmbientLease());

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
            Assert.Null(NewDbContextLock(contextLessLockMock.Object).TryGetAmbientLease());
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
            await dbContextLock.TryReleaseAsync("owner");

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

        private DbContextLock NewDbContextLock(IExecutionContext executionContext, string? lockId = null) =>
            new(lockCollectionMock.Object,
                lockId ?? LockId,
                executionContext,
                new Mock<ILogger>().Object);

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
            SetupLeaseStamp(NewLeaseDocument(DbContextLock.DefaultLeaseDuration));

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

        private static async Task WaitForUpdatesAsync(
            ConcurrentQueue<UpdateDefinition<BsonDocument>> updates,
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
