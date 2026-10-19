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
using Etherna.Scrinium.Core.Repositories;
using Moq;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.Core.Utility
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
        public async Task ConcurrentFirstHandlersOnSharedContextResolveEachInItsFlow()
        {
            /* Parallel flows sharing one execution context (the items of an HTTP request, or
             * an async local context initialized before forking) can construct their first
             * handler concurrently: the stack claim on the shared (not thread safe) items
             * dictionary must be atomic, and each flow must resolve the handler it pushed,
             * never the one of a sibling flow. */
            const int flowsCount = 4;
            const int attempts = 1000;

            //materialize the mocked engines before the parallel flows access them
            var engines = Enumerable.Range(0, flowsCount)
                .Select(_ =>
                {
                    var flowEngineMock = new Mock<IDbContextEngine>();
                    flowEngineMock.Setup(e => e.ExecutionContext)
                        .Returns(AsyncLocalContext.Instance);
                    return flowEngineMock.Object;
                })
                .ToArray();

            for (int i = 0; i < attempts; i++)
            {
                // Setup.
                using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
                using var barrier = new Barrier(flowsCount);

                // Action.
                //the spawned tasks inherit the context initialized by the test flow
                var resolvedEngines = await Task.WhenAll(
                    engines.Select(engine => Task.Run(() =>
                    {
                        barrier.SignalAndWait();
                        using (new DbExecutionContextHandler(engine))
                            return DbExecutionContextHandler.TryGetCurrentDbContextEngine(AsyncLocalContext.Instance);
                    })));

                // Assert.
                //a single stack registered into the shared items, each flow resolving its own handler
                Assert.Single(AsyncLocalContext.Instance.Items!);
                Assert.Equal(engines, resolvedEngines);
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

        [Fact]
        public async Task SiblingFlowRepositoryLessHandlerDoesNotShadowTheFlowOwnRepository()
        {
            /* The scenario of a request running repository reads in parallel: a flow
             * deserializing inside the handler of its repository, while a sibling flow of the
             * same request pushed a handler without repository after it. The flow must keep
             * resolving its own repository. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var engine = engineMock.Object;
            var repository = new Mock<IRepository>().Object;
            var readingFlowPushed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var siblingFlowPushed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            // Action.
            var readingFlow = Task.Run(async () =>
            {
                using (new DbExecutionContextHandler(engine, repository))
                {
                    readingFlowPushed.SetResult();
                    await siblingFlowPushed.Task;
                    return DbExecutionContextHandler.TryGetCurrentRepository(AsyncLocalContext.Instance);
                }
            });
            var siblingFlow = Task.Run(async () =>
            {
                await readingFlowPushed.Task;
                using (new DbExecutionContextHandler(engine))
                {
                    siblingFlowPushed.SetResult();
                    await readingFlow;
                }
            });
            await siblingFlow;

            // Assert.
            Assert.Same(repository, await readingFlow);
        }
    }
}
