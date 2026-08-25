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
using Etherna.MongoDB.Driver.Linq;
using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Etherna.Scrinium.Core.Utility;
using Etherna.Scrinium.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.IntegrationTests
{
    /* Application resource locks persist one lease document per resource, identified by
     * their namespaced string id in the same collection of the db context lock: these tests
     * manipulate the documents raw, simulating dead holders and concurrent instances. */
    [Collection("Integration")]
    public class ResourceLockTests : IAsyncLifetime
    {
        // Consts.
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

        // Fields.
        private readonly IntegrationFixture fixture;
        private readonly string lockCollectionName;
        private readonly IMigrationsDbContext migrationsDbContext;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        public ResourceLockTests(IntegrationFixture fixture)
        {
            this.fixture = fixture;
            serviceScope = fixture.ServiceProvider.CreateScope();
            migrationsDbContext = serviceScope.ServiceProvider.GetRequiredService<IMigrationsDbContext>();
            lockCollectionName = migrationsDbContext.Engine.Options.DbLockCollectionName;
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            /* Unconditional teardown: a lease left behind by a failing test would deny the
             * claims of the next ones for its whole duration, reporting their failures as
             * unrelated denials. Resource lock ids carry the namespace separator, so the db
             * context lock document of other tests stays untouched. */
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await AccessLockCollectionAsync(lockCollection => lockCollection.DeleteManyAsync(
                Builders<BsonDocument>.Filter.Regex("_id", new BsonRegularExpression("/", ""))));

            serviceScope.Dispose();
        }

        // Tests.
        [Fact]
        public async Task ClaimsEnsureTheTtlIndexOfTheLockCollection()
        {
            /* The TTL index collects the documents abandoned by dead owners, after their
             * retention: without it, high cardinality locks would grow the collection
             * forever. The first claim of the engine creates it. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var testEngine = fixture.TestDbContext.Engine;

            // Action.
            var lease = await fixture.TestDbContext.TryAcquireResourceLockAsync("ttl-probes", "resource");

            // Assert.
            Assert.NotNull(lease);
            await lease.DisposeAsync();

            var lockIndexes = await (await testEngine.Client
                .GetDatabase(testEngine.Options.DbName)
                .GetCollection<BsonDocument>(testEngine.Options.DbLockCollectionName)
                .Indexes.ListAsync()).ToListAsync();
            var ttlIndex = Assert.Single(lockIndexes, index =>
                index["name"].AsString == ResourceLock.AbandonedDocumentsTtlIndexName);
            Assert.Equal(
                ResourceLock.AbandonedDocumentRetention.TotalSeconds,
                ttlIndex["expireAfterSeconds"].ToDouble());
        }

        [Fact]
        public async Task ConcurrentExclusiveAcquiresElectOneHolder()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            // Action.
            //application instances racing on the same free resource
            var leases = await RunConcurrentlyAsync(8, async () =>
            {
                using var flowContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
                return await migrationsDbContext.TryAcquireResourceLockAsync("pins", "concurrent-exclusive");
            });

            // Assert.
            //the claim is atomic on the server: a single acquirer wins
            var wonLease = Assert.Single(leases, lease => lease is not null);
            await wonLease!.DisposeAsync();
        }

        [Fact]
        public async Task ConcurrentSharedAcquiresAdmitEveryHolder()
        {
            /* Shared acquisitions racing on the insert of the first lease collide on the id
             * index: the losers must retry onto the created document, and every holder must
             * end admitted. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            // Action.
            var leases = await RunConcurrentlyAsync(8, async () =>
            {
                using var flowContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
                return await migrationsDbContext.TryAcquireResourceLockAsync("pins", "concurrent-shared", ResourceLockMode.Shared);
            });

            // Assert.
            Assert.All(leases, lease => Assert.NotNull(lease));
            var lockDocument = await TryFindResourceLockDocumentAsync("pins", "concurrent-shared");
            Assert.NotNull(lockDocument);
            Assert.Equal(leases.Length, lockDocument["SharedLeases"].AsBsonArray.Count);

            //the last release deletes the document
            foreach (var lease in leases)
                await lease!.DisposeAsync();
            Assert.Null(await TryFindResourceLockDocumentAsync("pins", "concurrent-shared"));
        }

        [Fact]
        public async Task DeadSharedHolderExpiresAloneAndIsPurgedByTheNextRelease()
        {
            /* A dead holder must not lock the resource forever, nor need any cleanup task:
             * its lease expires alone, and the next release drops the expired entry. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var resourceLock = migrationsDbContext.Engine.GetResourceLock("pins", "dead-holder");
            var liveLease = await resourceLock.TryAcquireAsync(ResourceLockMode.Shared);
            Assert.NotNull(liveLease);

            //a dead holder left its expired lease entry on the document
            await PushSharedLeaseEntryAsync("pins", "dead-holder", new BsonDocument
            {
                ["ExpirationTime"] = DateTime.UtcNow - TimeSpan.FromMinutes(1),
                ["LeaseId"] = "dead-holder-lease",
                ["Owner"] = "dead-holder-instance"
            });

            // Action.
            //the live holder still locks the resource against exclusive claims
            Assert.Null(await resourceLock.TryAcquireAsync());
            await liveLease.DisposeAsync();

            // Assert.
            //the release dropped the own lease and the expired one with it, emptying the document
            Assert.Null(await TryFindResourceLockDocumentAsync("pins", "dead-holder"));
            var exclusiveLease = await resourceLock.TryAcquireAsync();
            Assert.NotNull(exclusiveLease);
            await exclusiveLease.DisposeAsync();
        }

        [Fact]
        public async Task ExclusiveAcquireExcludesEveryOtherClaim()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var resourceLock = migrationsDbContext.Engine.GetResourceLock("pins", "exclusive");

            // Action.
            var lease = await resourceLock.TryAcquireAsync();

            // Assert.
            Assert.NotNull(lease);
            Assert.True(await resourceLock.IsLockedAsync());
            //the acquired document carries the namespaced id of the resource
            var lockDocument = await TryFindResourceLockDocumentAsync("pins", "exclusive");
            Assert.NotNull(lockDocument);
            Assert.Equal(lease.OwnerId, lockDocument["Owner"].AsString);

            //any other claim is denied, exclusive or shared
            Assert.Null(await resourceLock.TryAcquireAsync());
            Assert.Null(await resourceLock.TryAcquireAsync(ResourceLockMode.Shared));

            //the release deletes the document: the lock opens right away
            await lease.DisposeAsync();
            Assert.Null(await TryFindResourceLockDocumentAsync("pins", "exclusive"));
            Assert.False(await resourceLock.IsLockedAsync());
        }

        [Fact]
        public async Task ExclusiveAcquireTakesOverAFullyExpiredSharedDocument()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            //every shared holder of the resource died without releasing
            await InsertResourceLockDocumentAsync(new BsonDocument
            {
                ["_id"] = ResourceLockId("pins", "expired-shared"),
                ["ExpirationTime"] = DateTime.UtcNow - TimeSpan.FromMinutes(1),
                ["SharedLeases"] = new BsonArray
                {
                    new BsonDocument
                    {
                        ["ExpirationTime"] = DateTime.UtcNow - TimeSpan.FromMinutes(1),
                        ["LeaseId"] = "dead-holder-lease",
                        ["Owner"] = "dead-holder-instance"
                    }
                }
            });

            // Action.
            var lease = await migrationsDbContext.TryAcquireResourceLockAsync("pins", "expired-shared");

            // Assert.
            //the takeover drops the dead holder entries, leaving a plain exclusive lease
            Assert.NotNull(lease);
            var lockDocument = await TryFindResourceLockDocumentAsync("pins", "expired-shared");
            Assert.NotNull(lockDocument);
            Assert.Equal(lease.OwnerId, lockDocument["Owner"].AsString);
            Assert.False(lockDocument.Contains("SharedLeases"));

            await lease.DisposeAsync();
        }

        [Fact]
        public async Task ResourceLocksAreIsolatedByNamespaceAndResource()
        {
            /* Locks of different namespaces, or of different resources of one namespace, are
             * independent documents: exclusive holders coexist on all of them, and none
             * interferes with the db context lock. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var engine = migrationsDbContext.Engine;

            // Action.
            var pinALease = await migrationsDbContext.TryAcquireResourceLockAsync("pins", "A");
            var pinBLease = await migrationsDbContext.TryAcquireResourceLockAsync("pins", "B");
            var pushALease = await migrationsDbContext.TryAcquireResourceLockAsync("pushes", "A");
            var dbContextLockClaimed = await engine.DbContextLock.TryClaimAsync("isolation-owner");

            // Assert.
            Assert.NotNull(pinALease);
            Assert.NotNull(pinBLease);
            Assert.NotNull(pushALease);
            Assert.True(dbContextLockClaimed);

            await pinALease.DisposeAsync();
            await pinBLease.DisposeAsync();
            await pushALease.DisposeAsync();
            await engine.DbContextLock.TryReleaseAsync("isolation-owner");
        }

        [Fact]
        public async Task SharedAcquirePurgesTheExpiredEntriesOfDeadHolders()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            //a fully expired shared document, left by holders that died without releasing
            await InsertResourceLockDocumentAsync(new BsonDocument
            {
                ["_id"] = ResourceLockId("pins", "purged"),
                ["ExpirationTime"] = DateTime.UtcNow - TimeSpan.FromMinutes(1),
                ["SharedLeases"] = new BsonArray
                {
                    new BsonDocument
                    {
                        ["ExpirationTime"] = DateTime.UtcNow - TimeSpan.FromMinutes(1),
                        ["LeaseId"] = "dead-holder-lease",
                        ["Owner"] = "dead-holder-instance"
                    }
                }
            });

            // Action.
            var lease = await migrationsDbContext.TryAcquireResourceLockAsync("pins", "purged", ResourceLockMode.Shared);

            // Assert.
            //the acquisition appended its own lease, dropping the expired one in the same write
            Assert.NotNull(lease);
            var lockDocument = await TryFindResourceLockDocumentAsync("pins", "purged");
            Assert.NotNull(lockDocument);
            var sharedLease = Assert.Single(lockDocument["SharedLeases"].AsBsonArray).AsBsonDocument;
            Assert.Equal(lease.OwnerId, sharedLease["Owner"].AsString);

            await lease.DisposeAsync();
        }

        [Fact]
        public async Task SharedAcquireTakesOverAnExpiredExclusiveLease()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            //an exclusive holder died without releasing: its lease expired alone
            await InsertResourceLockDocumentAsync(new BsonDocument
            {
                ["_id"] = ResourceLockId("pins", "expired-exclusive"),
                ["Owner"] = "dead-instance",
                ["ExpirationTime"] = DateTime.UtcNow - TimeSpan.FromMinutes(1),
                ["LeaseDurationTicks"] = TimeSpan.FromMinutes(10).Ticks
            });

            // Action.
            var lease = await migrationsDbContext.TryAcquireResourceLockAsync("pins", "expired-exclusive", ResourceLockMode.Shared);

            // Assert.
            //the takeover clears the dead ownership, leaving a plain shared document
            Assert.NotNull(lease);
            var lockDocument = await TryFindResourceLockDocumentAsync("pins", "expired-exclusive");
            Assert.NotNull(lockDocument);
            Assert.False(lockDocument.Contains("Owner"));
            Assert.Single(lockDocument["SharedLeases"].AsBsonArray);

            await lease.DisposeAsync();
        }

        [Fact]
        public async Task SharedHoldersCoexistAndExcludeTheExclusiveClaim()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var resourceLock = migrationsDbContext.Engine.GetResourceLock("pins", "shared");

            // Action.
            var firstLease = await resourceLock.TryAcquireAsync(ResourceLockMode.Shared);
            var secondLease = await resourceLock.TryAcquireAsync(ResourceLockMode.Shared);

            // Assert.
            Assert.NotNull(firstLease);
            Assert.NotNull(secondLease);
            var lockDocument = await TryFindResourceLockDocumentAsync("pins", "shared");
            Assert.NotNull(lockDocument);
            Assert.Equal(2, lockDocument["SharedLeases"].AsBsonArray.Count);

            //any live shared holder denies the exclusive claim
            Assert.Null(await resourceLock.TryAcquireAsync());
            await firstLease.DisposeAsync();
            Assert.Null(await resourceLock.TryAcquireAsync());

            //the last release deletes the document, opening the lock right away
            await secondLease.DisposeAsync();
            Assert.Null(await TryFindResourceLockDocumentAsync("pins", "shared"));
            var exclusiveLease = await resourceLock.TryAcquireAsync();
            Assert.NotNull(exclusiveLease);
            await exclusiveLease.DisposeAsync();
        }

        [Fact]
        public async Task SharedRenewalsKeepTheLeaseAliveBeyondItsDuration()
        {
            /* The lease duration is not the duration of the work: the background renewals
             * keep the holder's entry, and the top level expiration, always in the future. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var claimedLeaseDuration = TimeSpan.FromSeconds(2);

            // Action.
            var lease = await migrationsDbContext.TryAcquireResourceLockAsync(
                "pins", "renewed", ResourceLockMode.Shared, claimedLeaseDuration);
            Assert.NotNull(lease);
            var acquiredDocument = await TryFindResourceLockDocumentAsync("pins", "renewed");
            Assert.NotNull(acquiredDocument);
            var acquiredExpiration = acquiredDocument["ExpirationTime"].ToUniversalTime();

            await Task.Delay(claimedLeaseDuration);

            // Assert.
            //past the claimed duration, the renewals pushed the expiration forward
            Assert.True(await migrationsDbContext.IsResourceLockedAsync("pins", "renewed"));
            var renewedDocument = await TryFindResourceLockDocumentAsync("pins", "renewed");
            Assert.NotNull(renewedDocument);
            Assert.True(renewedDocument["ExpirationTime"].ToUniversalTime() > acquiredExpiration);

            await lease.DisposeAsync();
        }

        // Helpers.
        private Task<T> AccessLockCollectionAsync<T>(Func<IMongoCollection<BsonDocument>, Task<T>> action) =>
            migrationsDbContext.Notes.AccessToCollectionAsync(collection =>
                action(collection.Database.GetCollection<BsonDocument>(lockCollectionName)));

        private Task InsertResourceLockDocumentAsync(BsonDocument lockDocument) =>
            AccessLockCollectionAsync(async lockCollection =>
            {
                await lockCollection.InsertOneAsync(lockDocument);
                return 0;
            });

        private Task PushSharedLeaseEntryAsync(string resourceNamespace, string resourceId, BsonDocument leaseEntry) =>
            AccessLockCollectionAsync(lockCollection => lockCollection.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", ResourceLockId(resourceNamespace, resourceId)),
                Builders<BsonDocument>.Update.Push("SharedLeases", leaseEntry)));

        private static string ResourceLockId(string resourceNamespace, string resourceId) =>
            resourceNamespace + "/" + resourceId;

        /* Release every flow at once: without a rendezvous the tasks could run one after the
         * other, and the test would prove nothing about the concurrent case. */
        private static async Task<T[]> RunConcurrentlyAsync<T>(int flowsCount, Func<Task<T>> flow)
        {
            using var readySignal = new CountdownEvent(flowsCount);
            var startSignal = new TaskCompletionSource();

            var flowTasks = Enumerable.Range(0, flowsCount).Select(_ => Task.Run(async () =>
            {
                readySignal.Signal();
                await startSignal.Task;
                return await flow();
            })).ToArray();

            Assert.True(readySignal.Wait(TestTimeout));
            startSignal.SetResult();

            return await Task.WhenAll(flowTasks).WaitAsync(TestTimeout);
        }

        private Task<BsonDocument?> TryFindResourceLockDocumentAsync(string resourceNamespace, string resourceId) =>
            AccessLockCollectionAsync(async lockCollection =>
                (BsonDocument?)await (await lockCollection.FindAsync(
                    Builders<BsonDocument>.Filter.Eq("_id", ResourceLockId(resourceNamespace, resourceId)))).FirstOrDefaultAsync());
    }
}
