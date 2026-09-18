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

using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.Exceptions;
using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Etherna.Scrinium.Core.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Etherna.Scrinium.AspNetCore.Extensions
{
    public static class ApplicationBuilderExtensions
    {
        /// <summary>
        /// Seed every registered db context still not seeded, blocking the application startup
        /// until they all complete. A db context seeds after the children it declares with
        /// <see cref="DbContextOptions.ParentFor{TDbContext}"/>, so its seed finds them seeded,
        /// and the db contexts not depending on each other seed in parallel. Each seeding runs
        /// on a scope of its own.
        /// </summary>
        /// <param name="builder">The application builder</param>
        /// <param name="lockWaitTimeout">Maximum time each seeding waits for the db context lock
        /// held by ANOTHER owner, forwarded to
        /// <see cref="IDbContext.SeedIfNeededAsync(TimeSpan?, TimeSpan?)"/> and defaulted by it
        /// to the lease duration of the seeding</param>
        /// <param name="lockLeaseDuration">Duration of the lock lease claimed by EACH seeding,
        /// forwarded to <see cref="IDbContext.SeedIfNeededAsync(TimeSpan?, TimeSpan?)"/> and
        /// defaulted by it to <see cref="Core.Utility.ResourceLock.DefaultLeaseDuration"/>: how
        /// long a db context stays locked if this application instance dies before its seeding
        /// completes</param>
        /// <returns>The application builder</returns>
        public static IApplicationBuilder SeedDbContexts(
            this IApplicationBuilder builder,
            TimeSpan? lockWaitTimeout = null,
            TimeSpan? lockLeaseDuration = null)
        {
            ArgumentNullException.ThrowIfNull(builder);

            var serviceProvider = builder.ApplicationServices;
            var scriniumOptions = serviceProvider.GetRequiredService<IOptions<ScriniumOptions>>();
            var dbContextRegistrations = serviceProvider.GetServices<DbContextRegistration>().ToArray();

            // Seed, each db context after its children and the independent ones in parallel.
            /* A seeding holds the exclusive access of its engine, and the saves of a parent
             * cascade into its children: a parent seeding beside one of its children would
             * write into an engine whose exclusive window belongs to another flow. Seeding
             * the children first also lets the seed of a parent rely on what they seeded.
             * A declared child type resolves to its registration like the scope attach
             * resolves its instance, and the registrations never close a cycle. */
            Dictionary<DbContextRegistration, Task> seedingTasks = [];
            DbContextRegistration GetRegistration(Type dbContextType) =>
                dbContextRegistrations.Last(registration =>
                    registration.ServiceType == dbContextType ||
                    registration.ImplementationType == dbContextType);
            Task GetSeedingTask(DbContextRegistration registration)
            {
                if (!seedingTasks.TryGetValue(registration, out var seedingTask))
                {
                    var childSeedingTasks = registration.Options.ChildDbContextTypes
                        .Select(GetRegistration)
                        .Distinct()
                        .ToDictionary(childRegistration => childRegistration, GetSeedingTask);

                    seedingTask = SeedDbContextAsync(
                        serviceProvider,
                        registration,
                        childSeedingTasks,
                        lockWaitTimeout,
                        lockLeaseDuration);
                    seedingTasks[registration] = seedingTask;
                }

                return seedingTask;
            }

            Task.WaitAll(scriniumOptions.Value.DbContextTypes
                .Select(GetRegistration)
                .Select(GetSeedingTask)
                .ToArray());

            return builder;
        }

        // Helpers.
        private static async Task SeedDbContextAsync(
            IServiceProvider serviceProvider,
            DbContextRegistration registration,
            Dictionary<DbContextRegistration, Task> childSeedingTasks,
            TimeSpan? lockWaitTimeout,
            TimeSpan? lockLeaseDuration)
        {
            // Wait for the seedings of the children, denying this one when any of them failed.
            /* Each failed seeding reports its own error: this one reports what it didn't run
             * for, instead of seeding over children left without their seed. */
            await Task.WhenAll(childSeedingTasks.Values).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            var failedChildDbContextNames = childSeedingTasks
                .Where(pair => !pair.Value.IsCompletedSuccessfully)
                .Select(pair => pair.Key.ImplementationType.Name)
                .ToArray();
            if (failedChildDbContextNames.Length > 0)
                throw new ScriniumDbSeedingException(
                    $"Can't seed {registration.ImplementationType.Name} dbContext: the seeding of its child db contexts " +
                    $"{string.Join(", ", failedChildDbContextNames)} failed");

            /* An execution context serves a single flow, and a scope a single unit of work:
             * seeding inside shared ones would share the ambient db state and the db context
             * instances, the attached children included, between the parallel seeds. */
            using var execContext = AsyncLocalContext.Instance.InitAsyncLocalContext();
            using var serviceScope = serviceProvider.CreateScope();
            var dbContext = (IDbContext)serviceScope.ServiceProvider.GetRequiredService(registration.ServiceType);
            await dbContext.SeedIfNeededAsync(lockWaitTimeout, lockLeaseDuration).ConfigureAwait(false);
        }
    }
}
