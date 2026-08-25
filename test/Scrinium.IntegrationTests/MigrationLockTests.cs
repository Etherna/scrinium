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
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Etherna.Scrinium.Core.Utility;
using Etherna.Scrinium.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.IntegrationTests
{
    /* The db context lock persists one lease document per db context in the lock collection:
     * these tests manipulate it raw, simulating dead owners and other application instances
     * connected to the same database. */
    [Collection("Integration")]
    public class MigrationLockTests : IAsyncLifetime
    {
        // Consts.
        private const string ForeignOwnerId = "another-instance";
        /* The seedings driven against a held lock ask for a short wait: it shortens their
         * retry delay too, keeping them fast. */
        private static readonly TimeSpan SeedingLockWait = TimeSpan.FromSeconds(5);
        private const string SeedingWaitingForLockEventName = "DbContextSeedingWaitingForLock";
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

        // Fields.
        private readonly IntegrationFixture fixture;
        private readonly string lockCollectionName;
        private readonly IMigrationsDbContext migrationsDbContext;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public MigrationLockTests(IntegrationFixture fixture)
        {
            this.fixture = fixture;
            serviceScope = fixture.ServiceProvider.CreateScope();
            migrationsDbContext = serviceScope.ServiceProvider.GetRequiredService<IMigrationsDbContext>();
            lockCollectionName = migrationsDbContext.Engine.Options.DbLockCollectionName;
        }

        public Task InitializeAsync()
        {
            fixture.MigrationsLogEvents.Clear();
            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            /* Unconditional teardown: a lease left behind by a failing test would deny every
             * migration and seeding of the next ones for its whole duration, reporting their
             * failures as unrelated denials. */
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await DeleteLockDocumentAsync();
            await DeleteMigrationOperationsAsync();

            serviceScope.Dispose();
        }

        // Tests.
        [Fact]
        public async Task AlreadySeededDbContextClaimsNoLock()
        {
            /* Every application instance restart calls the seeding: an already seeded db
             * context must not even reach the lock collection. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await migrationsDbContext.SeedIfNeededAsync();
            await DropLockCollectionAsync();

            // Action.
            var seeded = await migrationsDbContext.SeedIfNeededAsync();

            // Assert.
            Assert.False(seeded);
            Assert.DoesNotContain(lockCollectionName, await ListCollectionNamesAsync());
        }

        [Fact]
        public async Task ClaimedLeaseDurationDrivesTheRenewalsOfAnotherInstance()
        {
            /* The claimer and the renewer are frequently different processes: a dashboard web
             * process claims the lock starting a migration, and the background worker resumes
             * it. The duration chosen by the claim travels in the lease document, so the
             * renewals of the other process run on it. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var claimedLeaseDuration = TimeSpan.FromSeconds(2);
            Assert.True(await migrationsDbContext.Engine.DbContextLock.TryClaimAsync(
                ForeignOwnerId, claimedLeaseDuration));

            //the lock of the other process: it claimed nothing, it only resumes the claim
            var otherInstanceLock = new ResourceLock(
                await AccessLockCollectionAsync(Task.FromResult),
                migrationsDbContext.Engine.Identifier,
                AsyncLocalContext.Instance,
                new LoggerFactory().CreateLogger<ResourceLock>());

            // Action.
            var lease = await otherInstanceLock.TryResumeClaimAsync(ForeignOwnerId);

            // Assert.
            Assert.NotNull(lease);
            var resumedDocument = await TryFindLockDocumentAsync();
            Assert.NotNull(resumedDocument);
            Assert.Equal(claimedLeaseDuration.Ticks, resumedDocument["LeaseDurationTicks"].AsInt64);
            //the resumed lease expires on the claimed duration, not on the default one
            var resumedExpiration = resumedDocument["ExpirationTime"].ToUniversalTime();
            Assert.InRange(resumedExpiration, DateTime.UtcNow, DateTime.UtcNow + claimedLeaseDuration);

            //and the background renewals keep pushing it forward, at a fraction of that duration
            await Task.Delay(claimedLeaseDuration);
            var renewedDocument = await TryFindLockDocumentAsync();
            Assert.NotNull(renewedDocument);
            Assert.True(renewedDocument["ExpirationTime"].ToUniversalTime() > resumedExpiration);

            await lease.DisposeAsync();
            Assert.Null(await TryFindLockDocumentAsync());
        }

        [Fact]
        public async Task ConcurrentMigrationStartsElectSingleOperation()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            migrationsDbContext.DocumentMigrations = [];
            await DeleteMigrationOperationsAsync();

            // Action.
            //start concurrently from separate scopes, like parallel dashboard requests would do
            var startedOps = await RunConcurrentlyAsync(8, async () =>
            {
                using var startContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
                using var startScope = fixture.ServiceProvider.CreateScope();
                var startDbContext = startScope.ServiceProvider.GetRequiredService<IMigrationsDbContext>();
                return await startDbContext.TryStartMigrationAsync();
            });

            // Assert.
            //the claim is atomic on the server: a single operation wins, the losers delete themselves
            var electedOp = Assert.Single(startedOps, op => op is not null);
            Assert.Equal(1, await CountMigrationOperationsAsync());

            //cleanup: execute the elected operation, completing it and releasing the claim
            await migrationsDbContext.ExecuteMigrationAsync(electedOp!.Id);
            var completedOp = await migrationsDbContext.GetMigrationAsync(electedOp.Id);
            Assert.Equal(DbMigrationOperation.Status.Completed, completedOp.CurrentStatus);
        }

        [Fact]
        public async Task ConcurrentMigrationStartsElectSingleOperationOverAnExpiredLock()
        {
            /* Claimers taking over an expired lease all match its expiration, differently from
             * the claimers of a free lock, that all collide inserting it: the server serializes
             * them on the document, and the losers find the lease of the winner. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            migrationsDbContext.DocumentMigrations = [];
            await DeleteMigrationOperationsAsync();
            //an expired lease of another instance, still on the lock
            await InsertLockDocumentAsync(DateTime.UtcNow - TimeSpan.FromMinutes(1));

            // Action.
            var startedOps = await RunConcurrentlyAsync(8, async () =>
            {
                using var startContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
                using var startScope = fixture.ServiceProvider.CreateScope();
                var startDbContext = startScope.ServiceProvider.GetRequiredService<IMigrationsDbContext>();
                return await startDbContext.TryStartMigrationAsync();
            });

            // Assert.
            //a single operation takes the expired lease over, the losers delete themselves
            var electedOp = Assert.Single(startedOps, op => op is not null);
            Assert.Equal(1, await CountMigrationOperationsAsync());

            //cleanup: execute the elected operation, completing it and releasing the claim
            await migrationsDbContext.ExecuteMigrationAsync(electedOp!.Id);
            var completedOp = await migrationsDbContext.GetMigrationAsync(electedOp.Id);
            Assert.Equal(DbMigrationOperation.Status.Completed, completedOp.CurrentStatus);
        }

        [Fact]
        public async Task ConcurrentSeedingsSeedOnce()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await ResetSeedingStateAsync();

            // Action.
            //application instances starting together on the same unseeded database
            var seedingResults = await RunConcurrentlyAsync(8, async () =>
            {
                using var seedingContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
                using var seedingScope = fixture.ServiceProvider.CreateScope();
                var seedingDbContext = seedingScope.ServiceProvider.GetRequiredService<IMigrationsDbContext>();
                return await seedingDbContext.SeedIfNeededAsync(SeedingLockWait);
            });

            // Assert.
            //the lock elects a single seeder: the others read the seeded state and skip
            Assert.Single(seedingResults, seeded => seeded);
            Assert.Equal(1, await CountSeedOperationsAsync());
            Assert.Null(await TryFindLockDocumentAsync());
        }

        [Fact]
        public async Task LiveForeignLockDeniesMigrationStart()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await DeleteMigrationOperationsAsync();

            //another application instance is migrating this same database
            await InsertLockDocumentAsync(DateTime.UtcNow + TimeSpan.FromMinutes(10));

            // Action.
            var migrationOp = await migrationsDbContext.TryStartMigrationAsync();

            // Assert.
            //the start is denied, and its losing operation doesn't stay behind
            Assert.Null(migrationOp);
            Assert.Equal(0, await CountMigrationOperationsAsync());
            var lockDocument = await TryFindLockDocumentAsync();
            Assert.NotNull(lockDocument);
            Assert.Equal(ForeignOwnerId, lockDocument["Owner"].AsString);
        }

        [Fact]
        public async Task LockDocumentWithoutExpirationTimeIsReclaimable()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            //a lease document without a date expiration: hand edited, or partially written
            await InsertLockDocumentAsync(expirationTime: null);

            // Action.
            var claimed = await migrationsDbContext.Engine.DbContextLock.TryClaimAsync("recovering-instance");

            // Assert.
            //comparisons are type bracketed on the server: without a type predicate nothing
            //could ever match nor upsert over that document, deadlocking the lock forever
            Assert.True(claimed);
            var lockDocument = await TryFindLockDocumentAsync();
            Assert.NotNull(lockDocument);
            Assert.Equal("recovering-instance", lockDocument["Owner"].AsString);
        }

        [Fact]
        public async Task MigrationRestartsAfterOwnerLeaseExpires()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            migrationsDbContext.DocumentMigrations = [];
            await DeleteMigrationOperationsAsync();

            var firstOp = await migrationsDbContext.TryStartMigrationAsync();
            Assert.NotNull(firstOp);

            //while the claim is live, any new start is denied
            Assert.Null(await migrationsDbContext.TryStartMigrationAsync());

            //simulate the owner process dying before executing: nobody renews the lease, that expires
            await ExpireLockLeaseAsync();

            // Action.
            var secondOp = await migrationsDbContext.TryStartMigrationAsync();

            // Assert.
            //the expired lease is taken over, and the orphaned operation closes cancelled
            Assert.NotNull(secondOp);
            using var verifyScope = fixture.ServiceProvider.CreateScope();
            var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<IMigrationsDbContext>();
            Assert.Equal(
                DbMigrationOperation.Status.Cancelled,
                (await verifyDbContext.GetMigrationAsync(firstOp.Id)).CurrentStatus);

            //cleanup: execute the second operation, completing it and releasing the claim
            await migrationsDbContext.ExecuteMigrationAsync(secondOp.Id);
            Assert.Equal(
                DbMigrationOperation.Status.Completed,
                (await verifyDbContext.GetMigrationAsync(secondOp.Id)).CurrentStatus);
        }

        [Fact]
        public async Task OrphanedRunningOperationClosesFailedAtNextStart()
        {
            /* A killed process leaves its operation on running status: the next start must
             * close it, or the dashboard would report a migration in progress forever. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            migrationsDbContext.DocumentMigrations = [];
            await DeleteMigrationOperationsAsync();

            var runningOp = new DbMigrationOperation(migrationsDbContext.Engine);
            runningOp.TaskStarted("dead-task");
            await migrationsDbContext.DbOperations.CreateAsync(runningOp);

            // Action.
            var restartedOp = await migrationsDbContext.TryStartMigrationAsync();

            // Assert.
            //an interrupted migration failed: it never completed its steps
            Assert.NotNull(restartedOp);
            using var verifyScope = fixture.ServiceProvider.CreateScope();
            var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<IMigrationsDbContext>();
            Assert.Equal(
                DbMigrationOperation.Status.Failed,
                (await verifyDbContext.GetMigrationAsync(runningOp.Id)).CurrentStatus);

            //cleanup: execute the restarted operation, releasing the claim
            await migrationsDbContext.ExecuteMigrationAsync(restartedOp.Id);
        }

        [Fact]
        public async Task ResumedLeasesAreFencedAgainstEachOther()
        {
            /* An operation can be executed twice (a task runner delivering it twice, or a
             * manual requeue): the loser must not release the lease of the winner, freeing
             * the lock for a third instance while the winner is still migrating. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var dbContextLock = migrationsDbContext.Engine.DbContextLock;
            Assert.True(await dbContextLock.TryClaimAsync("twice-executed-operation"));

            // Action.
            var firstLease = await dbContextLock.TryResumeClaimAsync("twice-executed-operation");
            var secondLease = await dbContextLock.TryResumeClaimAsync("twice-executed-operation");
            Assert.NotNull(firstLease);
            Assert.NotNull(secondLease);
            await firstLease.DisposeAsync();

            // Assert.
            //the invalidated lease released nothing: the lock still denies the other claimers
            Assert.NotNull(await TryFindLockDocumentAsync());
            Assert.False(await dbContextLock.TryClaimAsync(ForeignOwnerId));

            await secondLease.DisposeAsync();
            Assert.Null(await TryFindLockDocumentAsync());
        }

        [Fact]
        public async Task SeedingReclaimsExpiredForeignLock()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await ResetSeedingStateAsync();

            //a dead application instance left an expired lease on the db context lock
            await InsertLockDocumentAsync(DateTime.UtcNow - TimeSpan.FromMinutes(1));

            // Action.
            using var seedingScope = fixture.ServiceProvider.CreateScope();
            var seedingDbContext = seedingScope.ServiceProvider.GetRequiredService<IMigrationsDbContext>();
            //bounded: a regressed takeover must fail the test, not hang it
            var seeded = await seedingDbContext.SeedIfNeededAsync().WaitAsync(TestTimeout);

            // Assert.
            //the expired foreign lease is taken over, and the lock releases at completion
            Assert.True(seeded);
            Assert.Null(await TryFindLockDocumentAsync());
        }

        [Fact]
        public async Task SeedingSkipsWhenAnotherInstanceSeeds()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await ResetSeedingStateAsync();

            //another application instance holds the db context lock with a live lease
            await InsertLockDocumentAsync(DateTime.UtcNow + TimeSpan.FromMinutes(10));
            var seedingTask = Task.Run(async () =>
            {
                using var seedingContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
                using var seedingScope = fixture.ServiceProvider.CreateScope();
                var seedingDbContext = seedingScope.ServiceProvider.GetRequiredService<IMigrationsDbContext>();
                return await seedingDbContext.SeedIfNeededAsync(SeedingLockWait);
            });
            try
            {
                // Action.
                //rendezvous on the seeding reporting its wait, instead of hoping it started
                await WaitForSeedingWaitAsync();
                await CreateForeignSeedOperationAsync();
                var seeded = await seedingTask.WaitAsync(TestTimeout);

                // Assert.
                //the db has been seeded by the lock owner: this seeding skips, leaving the foreign claim untouched
                Assert.False(seeded);
                Assert.True(fixture.MigrationsLogEvents.HasLogged(SeedingWaitingForLockEventName));
                var lockDocument = await TryFindLockDocumentAsync();
                Assert.NotNull(lockDocument);
                Assert.Equal(ForeignOwnerId, lockDocument["Owner"].AsString);
            }
            finally
            {
                await DeleteLockDocumentAsync();

                //drain the background seeding: a failure here must not leave it seeding after the cleanup
                await Task.WhenAny(seedingTask, Task.Delay(TestTimeout));
            }
        }

        // Helpers.
        private Task<T> AccessLockCollectionAsync<T>(Func<IMongoCollection<BsonDocument>, Task<T>> action) =>
            migrationsDbContext.Notes.AccessToCollectionAsync(collection =>
                action(collection.Database.GetCollection<BsonDocument>(lockCollectionName)));

        private Task<int> CountMigrationOperationsAsync() =>
            migrationsDbContext.DbOperations.QueryElementsAsync(elements =>
                elements.OfType<DbMigrationOperation>()
                        .Where(op => op.DbContextName == migrationsDbContext.Engine.Identifier)
                        .CountAsync());

        private Task<int> CountSeedOperationsAsync() =>
            migrationsDbContext.DbOperations.QueryElementsAsync(elements =>
                elements.OfType<SeedOperation>()
                        .Where(op => op.DbContextName == migrationsDbContext.Engine.Identifier)
                        .CountAsync());

        private async Task CreateForeignSeedOperationAsync()
        {
            using var scope = fixture.ServiceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IMigrationsDbContext>();
            await dbContext.DbOperations.CreateAsync(new SeedOperation(dbContext.Engine));
        }

        private Task DeleteLockDocumentAsync() =>
            AccessLockCollectionAsync(lockCollection => lockCollection.DeleteOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", migrationsDbContext.Engine.Identifier)));

        private Task DeleteMigrationOperationsAsync() =>
            migrationsDbContext.DbOperations.DeleteManyAsync(
                Builders<OperationBase>.Filter.OfType<DbMigrationOperation>(op =>
                    op.DbContextName == migrationsDbContext.Engine.Identifier));

        private Task DropLockCollectionAsync() =>
            migrationsDbContext.Notes.AccessToCollectionAsync(async collection =>
            {
                await collection.Database.DropCollectionAsync(lockCollectionName);
                return 0;
            });

        private Task ExpireLockLeaseAsync() =>
            AccessLockCollectionAsync(lockCollection => lockCollection.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", migrationsDbContext.Engine.Identifier),
                Builders<BsonDocument>.Update.Set("ExpirationTime", DateTime.UtcNow - TimeSpan.FromMinutes(1))));

        private Task InsertLockDocumentAsync(DateTime? expirationTime) =>
            AccessLockCollectionAsync(async lockCollection =>
            {
                await lockCollection.InsertOneAsync(new BsonDocument
                {
                    ["_id"] = migrationsDbContext.Engine.Identifier,
                    ["Owner"] = ForeignOwnerId,
                    ["ExpirationTime"] = expirationTime is null ? BsonNull.Value : expirationTime.Value
                });
                return 0;
            });

        private Task<List<string>> ListCollectionNamesAsync() =>
            migrationsDbContext.Notes.AccessToCollectionAsync(async collection =>
                await (await collection.Database.ListCollectionNamesAsync()).ToListAsync());

        private async Task ResetSeedingStateAsync()
        {
            await migrationsDbContext.DbOperations.DeleteManyAsync(
                Builders<OperationBase>.Filter.OfType<SeedOperation>());
            migrationsDbContext.Engine.IsSeededCache = null;
        }

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

        private Task<BsonDocument?> TryFindLockDocumentAsync() =>
            AccessLockCollectionAsync(async lockCollection =>
                (BsonDocument?)await (await lockCollection.FindAsync(
                    Builders<BsonDocument>.Filter.Eq("_id", migrationsDbContext.Engine.Identifier))).FirstOrDefaultAsync());

        private async Task WaitForSeedingWaitAsync()
        {
            var waitStart = DateTime.UtcNow;
            while (!fixture.MigrationsLogEvents.HasLogged(SeedingWaitingForLockEventName))
            {
                Assert.True(DateTime.UtcNow - waitStart < TestTimeout, "The seeding never waited on the db context lock");
                await Task.Delay(20);
            }
        }
    }
}
