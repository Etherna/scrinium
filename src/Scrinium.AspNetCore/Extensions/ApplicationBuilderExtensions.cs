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
using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Etherna.Scrinium.Core.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Etherna.Scrinium.AspNetCore.Extensions
{
    public static class ApplicationBuilderExtensions
    {
        /// <summary>
        /// Seed every registered db context still not seeded, in parallel, blocking the
        /// application startup until they all complete.
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
            var mongODMOptions = serviceProvider.GetRequiredService<IOptions<ScriniumOptions>>();

            // Get dbcontext instances from a dedicated scope.
            using var serviceScope = serviceProvider.CreateScope();
            var dbContextTypes = mongODMOptions.Value.DbContextTypes;
            var dbContexts = dbContextTypes.Select(type => (IDbContext)serviceScope.ServiceProvider.GetRequiredService(type));

            // Seed all dbcontexts in parallel, each inside its own execution context.
            Task.WaitAll(dbContexts
                .Select(dbContext => SeedDbContextAsync(dbContext, lockWaitTimeout, lockLeaseDuration))
                .ToArray());

            return builder;
        }

        // Helpers.
        private static async Task SeedDbContextAsync(
            IDbContext dbContext,
            TimeSpan? lockWaitTimeout,
            TimeSpan? lockLeaseDuration)
        {
            /* An execution context serves a single flow: seeding inside a shared one
             * would share the ambient db state between the parallel seeds. */
            using var execContext = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await dbContext.SeedIfNeededAsync(lockWaitTimeout, lockLeaseDuration).ConfigureAwait(false);
        }
    }
}
