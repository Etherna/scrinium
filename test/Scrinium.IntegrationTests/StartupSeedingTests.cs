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

using Etherna.Scrinium.AspNetCore.Extensions;
using Etherna.Scrinium.Core.Options;
using Etherna.Scrinium.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using Xunit;

namespace Etherna.Scrinium.IntegrationTests
{
    [Collection("Integration")]
    public class StartupSeedingTests(IntegrationFixture fixture)
    {
        // Tests.
        [Fact]
        public void SeedDbContextsRunsEachSeedInItsOwnExecutionContext()
        {
            // Setup.
            /* Restrict the application db context types to the two observer contexts,
             * keeping the other fixture db contexts out of the startup seeding. */
            var applicationBuilder = new ApplicationBuilder(new DbContextTypesOverrideServiceProvider(
                fixture.ServiceProvider,
                typeof(ISeedObserverOneDbContext),
                typeof(ISeedObserverTwoDbContext)));

            // Action.
            applicationBuilder.SeedDbContexts();

            // Assert.
            //both db contexts are seeded
            using var serviceScope = fixture.ServiceProvider.CreateScope();
            Assert.True(serviceScope.ServiceProvider.GetRequiredService<ISeedObserverOneDbContext>().IsSeeded);
            Assert.True(serviceScope.ServiceProvider.GetRequiredService<ISeedObserverTwoDbContext>().IsSeeded);

            //each seed ran inside its own execution context, not shared with its siblings
            var oneItems = fixture.SeedingObservations[nameof(SeedObserverOneDbContext)];
            var twoItems = fixture.SeedingObservations[nameof(SeedObserverTwoDbContext)];
            Assert.NotNull(oneItems);
            Assert.NotNull(twoItems);
            Assert.NotSame(oneItems, twoItems);
        }

        // Helpers.
        /// <summary>
        /// Delegates every resolution to the inner service provider, overriding only the
        /// MongODM options with the given db context types.
        /// </summary>
        private sealed class DbContextTypesOverrideServiceProvider(
            IServiceProvider innerServiceProvider,
            params Type[] dbContextTypes) : IServiceProvider
        {
            public object? GetService(Type serviceType)
            {
                if (serviceType == typeof(IOptions<MongODMOptions>))
                {
                    var mongODMOptions = new MongODMOptions();
                    ((IMongODMOptionsBuilder)mongODMOptions).SetDbContextTypes(dbContextTypes);
                    return Options.Create(mongODMOptions);
                }

                return innerServiceProvider.GetService(serviceType);
            }
        }
    }
}
