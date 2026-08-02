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

using Etherna.MongODM.Core;
using Etherna.MongODM.Core.ExecContext.AsyncLocal;
using Etherna.MongODM.Core.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Etherna.MongODM.AspNetCore.Extensions
{
    public static class ApplicationBuilderExtensions
    {
        public static IApplicationBuilder SeedDbContexts(
            this IApplicationBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            var serviceProvider = builder.ApplicationServices;
            var mongODMOptions = serviceProvider.GetRequiredService<IOptions<MongODMOptions>>();

            // Get dbcontext instances from a dedicated scope.
            using var serviceScope = serviceProvider.CreateScope();
            var dbContextTypes = mongODMOptions.Value.DbContextTypes;
            var dbContexts = dbContextTypes.Select(type => (IDbContext)serviceScope.ServiceProvider.GetRequiredService(type));

            // Seed all dbcontexts in parallel, each inside its own execution context.
            Task.WaitAll(dbContexts.Select(SeedDbContextAsync).ToArray());

            return builder;
        }

        // Helpers.
        private static async Task SeedDbContextAsync(IDbContext dbContext)
        {
            /* An execution context serves a single flow: seeding inside a shared one
             * would share the ambient db state between the parallel seeds. */
            using var execContext = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await dbContext.SeedIfNeededAsync().ConfigureAwait(false);
        }
    }
}
