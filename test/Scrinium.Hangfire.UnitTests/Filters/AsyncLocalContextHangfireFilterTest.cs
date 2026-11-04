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

using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Hangfire;
using Hangfire.Common;
using Hangfire.Server;
using Hangfire.Storage;
using Moq;
using System;
using Xunit;

namespace Etherna.Scrinium.HF.Filters
{
    public class AsyncLocalContextHangfireFilterTest
    {
        // Fields.
        private readonly Mock<IAsyncLocalContext> asyncLocalContextMock = new();
        private readonly AsyncLocalContextHangfireFilter filter;

        // Constructor.
        public AsyncLocalContextHangfireFilterTest()
        {
            filter = new AsyncLocalContextHangfireFilter(asyncLocalContextMock.Object);
        }

        // Tests.
        [Fact]
        public void OverlappedPerformancesOfTheSameJobOpenAndCloseTheirOwnContext()
        {
            // Setup.
            Mock<IAsyncLocalContextHandler> firstHandlerMock = new();
            Mock<IAsyncLocalContextHandler> secondHandlerMock = new();
            asyncLocalContextMock.SetupSequence(c => c.InitAsyncLocalContext())
                .Returns(firstHandlerMock.Object)
                .Returns(secondHandlerMock.Object);
            var firstPerforming = BuildPerformingContext("6ad63e24b54d54243003b018");
            var secondPerforming = BuildPerformingContext("6ad63e24b54d54243003b018");

            // Action.
            filter.OnPerforming(firstPerforming);
            filter.OnPerforming(secondPerforming); //the job re-fetched while the first performance still runs
            filter.OnPerformed(new PerformedContext(firstPerforming, null, false, null));

            // Assert.
            firstHandlerMock.Verify(h => h.Dispose(), Times.Once());
            secondHandlerMock.Verify(h => h.Dispose(), Times.Never());

            // Action.
            filter.OnPerformed(new PerformedContext(secondPerforming, null, false, null));

            // Assert.
            secondHandlerMock.Verify(h => h.Dispose(), Times.Once());
        }

        // Helpers.
        private static PerformingContext BuildPerformingContext(string jobId) =>
            new(new PerformContext(
                new Mock<JobStorage>().Object,
                new Mock<IStorageConnection>().Object,
                new BackgroundJob(jobId, Job.FromExpression(() => Console.WriteLine()), DateTime.UtcNow),
                new JobCancellationToken(false)));
    }
}
