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

using Etherna.Scrinium.AspNetCore.Extensions;
using Etherna.Scrinium.Core.Exceptions;
using Etherna.Scrinium.Core.Options;
using Etherna.Scrinium.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using Xunit;

namespace Etherna.Scrinium.IntegrationTests
{
    [Collection("Integration")]
    public class StartupSeedingTests(IntegrationFixture fixture)
    {
        // Tests.
        [Fact]
        public void SeedDbContextsDeniesParentsOfFailedChildren()
        {
            // Setup.
            var applicationBuilder = new ApplicationBuilder(new DbContextTypesOverrideServiceProvider(
                fixture.ServiceProvider,
                typeof(ISeedFailingParentDbContext),
                typeof(ISeedFailingChildDbContext)));

            // Action.
            var exception = Assert.Throws<AggregateException>(() => applicationBuilder.SeedDbContexts());

            // Assert.
            //the child reports its own failure, and the parent the child it didn't seed for
            Assert.Equal(2, exception.InnerExceptions.Count);
            Assert.All(exception.InnerExceptions, e => Assert.IsType<ScriniumDbSeedingException>(e));
            var parentException = Assert.Single(
                exception.InnerExceptions,
                e => e.Message.Contains(nameof(SeedFailingParentDbContext), StringComparison.Ordinal));
            Assert.Contains(nameof(SeedFailingChildDbContext), parentException.Message, StringComparison.Ordinal);

            //the parent never ran: neither its own seed, nor another seed of its child
            Assert.Equal(1, fixture.SeedingFamilyObservations[SeedFailingChildDbContext.SeedAttemptsKey]);
            using var serviceScope = fixture.ServiceProvider.CreateScope();
            Assert.False(serviceScope.ServiceProvider.GetRequiredService<ISeedFailingParentDbContext>().IsSeeded);
        }

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

        [Fact]
        public void SeedDbContextsSeedsChildrenBeforeTheirParents()
        {
            // Setup.
            /* A writable hierarchy of three levels, all unseeded on fresh databases. The
             * parents are listed first: the seeding order comes from the declared children,
             * not from the registration one. */
            var applicationBuilder = new ApplicationBuilder(new DbContextTypesOverrideServiceProvider(
                fixture.ServiceProvider,
                typeof(ISeedGrandparentDbContext),
                typeof(ISeedParentDbContext),
                typeof(ISeedChildDbContext)));

            // Action.
            applicationBuilder.SeedDbContexts();

            // Assert.
            //every db context is seeded
            using var serviceScope = fixture.ServiceProvider.CreateScope();
            Assert.True(serviceScope.ServiceProvider.GetRequiredService<ISeedChildDbContext>().IsSeeded);
            Assert.True(serviceScope.ServiceProvider.GetRequiredService<ISeedGrandparentDbContext>().IsSeeded);
            Assert.True(serviceScope.ServiceProvider.GetRequiredService<ISeedParentDbContext>().IsSeeded);

            //each seed found its child already seeded, out of its exclusive window
            Assert.Equal(true, fixture.SeedingFamilyObservations[SeedGrandparentDbContext.IsParentSeededKey]);
            var childNotes = Assert.IsType<List<string>>(
                fixture.SeedingFamilyObservations[SeedParentDbContext.ChildNotesKey]);
            Assert.Equal(["seeded by the child"], childNotes);

            //each seeding ran on its own scope: the parent never shared its child instance
            //with the flow seeding that child
            Assert.NotSame(
                fixture.SeedingFamilyObservations[SeedChildDbContext.SeedingInstanceKey],
                fixture.SeedingFamilyObservations[SeedParentDbContext.AttachedChildInstanceKey]);
        }

        // Helpers.
        /// <summary>
        /// Delegates every resolution to the inner service provider, overriding only the
        /// Scrinium options with the given db context types.
        /// </summary>
        private sealed class DbContextTypesOverrideServiceProvider(
            IServiceProvider innerServiceProvider,
            params Type[] dbContextTypes) : IServiceProvider
        {
            public object? GetService(Type serviceType)
            {
                if (serviceType == typeof(IOptions<ScriniumOptions>))
                {
                    var scriniumOptions = new ScriniumOptions();
                    ((IScriniumOptionsBuilder)scriniumOptions).SetDbContextTypes(dbContextTypes);
                    return Options.Create(scriniumOptions);
                }

                return innerServiceProvider.GetService(serviceType);
            }
        }
    }
}
