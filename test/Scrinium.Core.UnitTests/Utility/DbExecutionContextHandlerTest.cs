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

using Etherna.MongODM.Core.ExecContext.AsyncLocal;
using Moq;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.MongODM.Core.Utility
{
    public class DbExecutionContextHandlerTest
    {
        // Fields.
        private readonly Mock<IDbContextEngine> engineMock = new();
        private readonly Mock<IDbContextEngine> otherEngineMock = new();

        // Constructor.
        public DbExecutionContextHandlerTest()
        {
            engineMock.Setup(e => e.ExecutionContext)
                .Returns(AsyncLocalContext.Instance);
            otherEngineMock.Setup(e => e.ExecutionContext)
                .Returns(AsyncLocalContext.Instance);
        }

        // Tests.
        [Fact]
        public async Task ConcurrentFirstHandlersOnSharedContextRegisterAtomically()
        {
            /* Parallel flows sharing one execution context can construct their first handler
             * of the key concurrently: the bookkeeping list claim on the shared (not thread
             * safe) items dictionary must be atomic. */
            const int flowsCount = 4;
            const int attempts = 1000;

            //materialize the mocked engine before the parallel flows access it
            var engine = engineMock.Object;

            for (int i = 0; i < attempts; i++)
            {
                // Setup.
                using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
                using var barrier = new Barrier(flowsCount);

                // Action.
                //the spawned tasks inherit the context initialized by the test flow
                var handlers = await Task.WhenAll(
                    Enumerable.Range(0, flowsCount)
                        .Select(_ => Task.Run(() =>
                        {
                            barrier.SignalAndWait();
                            return new DbExecutionContextHandler(engine);
                        }))
                        .ToArray());

                // Assert.
                //all handlers registered into the single bookkeeping list of the context
                var items = AsyncLocalContext.Instance.Items!;
                var requests = (ICollection)Assert.Single(items).Value!;
                Assert.Equal(flowsCount, requests.Count);

                Assert.Same(engine, DbExecutionContextHandler.TryGetCurrentDbContextEngine(AsyncLocalContext.Instance));
                foreach (var handler in handlers)
                    handler.Dispose();
                Assert.Null(DbExecutionContextHandler.TryGetCurrentDbContextEngine(AsyncLocalContext.Instance));
            }
        }

        [Fact]
        public void HandlerInitializesMissingExecutionContext()
        {
            Assert.Null(AsyncLocalContext.Instance.Items);

            using (new DbExecutionContextHandler(engineMock.Object))
            {
                Assert.NotNull(AsyncLocalContext.Instance.Items);
                Assert.Same(engineMock.Object, DbExecutionContextHandler.TryGetCurrentDbContextEngine(AsyncLocalContext.Instance));
            }

            //the handler disposes the execution context created by itself
            Assert.Null(AsyncLocalContext.Instance.Items);
        }

        [Fact]
        public void MissingExecutionContextResolvesNoHandler()
        {
            Assert.Null(AsyncLocalContext.Instance.Items);
            Assert.Null(DbExecutionContextHandler.TryGetCurrentDbContext(AsyncLocalContext.Instance));
            Assert.Null(DbExecutionContextHandler.TryGetCurrentDbContextEngine(AsyncLocalContext.Instance));
            Assert.Null(DbExecutionContextHandler.TryGetCurrentRepository(AsyncLocalContext.Instance));
        }

        [Fact]
        public void NestedHandlersResolveLastAndDisposeRestoresOuter()
        {
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            using (new DbExecutionContextHandler(engineMock.Object))
            {
                using (new DbExecutionContextHandler(otherEngineMock.Object))
                {
                    Assert.Same(otherEngineMock.Object, DbExecutionContextHandler.TryGetCurrentDbContextEngine(AsyncLocalContext.Instance));
                }

                Assert.Same(engineMock.Object, DbExecutionContextHandler.TryGetCurrentDbContextEngine(AsyncLocalContext.Instance));
            }

            Assert.Null(DbExecutionContextHandler.TryGetCurrentDbContextEngine(AsyncLocalContext.Instance));
        }
    }
}
