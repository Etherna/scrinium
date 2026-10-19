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

using Etherna.MongoDB.Driver;
using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Moq;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.Core.Utility
{
    public class DbSessionHandlerTest
    {
        // Fields.
        private readonly Mock<IDbContextEngine> engineMock = new();
        private readonly Mock<IDbContextEngine> otherEngineMock = new();
        private readonly Mock<IClientSessionHandle> sessionMock = new();

        // Constructor.
        public DbSessionHandlerTest()
        {
            engineMock.Setup(e => e.ExecutionContext)
                .Returns(AsyncLocalContext.Instance);
            otherEngineMock.Setup(e => e.ExecutionContext)
                .Returns(AsyncLocalContext.Instance);
        }

        // Tests.
        [Fact]
        public void AbortDeferralNeedsAnAmbientHandler()
        {
            //without an ambient handler the operation persisted: there is nothing to undo
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            Assert.False(DbSessionHandler.TryDeferToTransactionAbort(engineMock.Object, () => { }));
        }

        [Fact]
        public void AmbientSessionIsResolvedOnlyForItsEngine()
        {
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            using (new DbSessionHandler(engineMock.Object, sessionMock.Object))
            {
                Assert.Same(sessionMock.Object, DbSessionHandler.TryGetCurrentSession(engineMock.Object));
                Assert.Null(DbSessionHandler.TryGetCurrentSession(otherEngineMock.Object));
            }

            Assert.Null(DbSessionHandler.TryGetCurrentSession(engineMock.Object));
        }

        [Fact]
        public void CommitDeferralNeedsAnAmbientHandler()
        {
            //without an ambient handler the bookkeeping stays with the caller
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            Assert.False(DbSessionHandler.TryDeferToTransactionCommit(engineMock.Object, () => { }));
        }

        [Fact]
        public async Task ConcurrentFirstHandlersOnSharedContextResolveEachInItsFlow()
        {
            /* Parallel flows sharing one execution context can construct their first handler
             * concurrently: the stack claim on the shared (not thread safe) items dictionary
             * must be atomic, and each flow must resolve the session it handles, never the one
             * of a sibling flow. */
            const int flowsCount = 4;
            const int attempts = 1000;

            //materialize the mocked engine and sessions before the parallel flows access them
            var engine = engineMock.Object;
            var sessions = Enumerable.Range(0, flowsCount)
                .Select(_ => new Mock<IClientSessionHandle>().Object)
                .ToArray();

            for (int i = 0; i < attempts; i++)
            {
                // Setup.
                using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
                using var barrier = new Barrier(flowsCount);

                // Action.
                //the spawned tasks inherit the context initialized by the test flow
                var resolvedSessions = await Task.WhenAll(
                    sessions.Select(session => Task.Run(() =>
                    {
                        barrier.SignalAndWait();
                        using (new DbSessionHandler(engine, session))
                            return DbSessionHandler.TryGetCurrentSession(engine);
                    })));

                // Assert.
                Assert.Equal(sessions, resolvedSessions);
                Assert.Null(DbSessionHandler.TryGetCurrentSession(engine));
            }
        }

        [Fact]
        public void DeferredActionsRunAtTheAmbientHandlerCommit()
        {
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var runActions = new List<int>();

            using var handler = new DbSessionHandler(engineMock.Object, sessionMock.Object);
            Assert.True(DbSessionHandler.TryDeferToTransactionCommit(engineMock.Object, () => runActions.Add(0)));
            Assert.True(DbSessionHandler.TryDeferToTransactionCommit(engineMock.Object, () => runActions.Add(1)));
            Assert.Empty(runActions);

            handler.RunCommitActions();

            Assert.Equal([0, 1], runActions);
        }

        [Fact]
        public void DeferredUndoRunsInReverseOrderAtTheAmbientHandlerAbort()
        {
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var runActions = new List<int>();

            using var handler = new DbSessionHandler(engineMock.Object, sessionMock.Object);
            Assert.True(DbSessionHandler.TryDeferToTransactionAbort(engineMock.Object, () => runActions.Add(0)));
            Assert.True(DbSessionHandler.TryDeferToTransactionAbort(engineMock.Object, () => runActions.Add(1)));
            Assert.Empty(runActions);

            handler.RunAbortActions();

            //the undo runs last registered first, unwinding what the operations did in order
            Assert.Equal([1, 0], runActions);
        }

        [Fact]
        public void HandlerInitializesMissingExecutionContext()
        {
            Assert.Null(AsyncLocalContext.Instance.Items);

            using (new DbSessionHandler(engineMock.Object, sessionMock.Object))
            {
                Assert.NotNull(AsyncLocalContext.Instance.Items);
                Assert.Same(sessionMock.Object, DbSessionHandler.TryGetCurrentSession(engineMock.Object));
            }

            //the handler disposes the execution context created by itself
            Assert.Null(AsyncLocalContext.Instance.Items);
        }

        [Fact]
        public void InnermostSessionWinsAndDisposeRestoresOuter()
        {
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var innerSessionMock = new Mock<IClientSessionHandle>();

            using (new DbSessionHandler(engineMock.Object, sessionMock.Object))
            {
                using (new DbSessionHandler(engineMock.Object, innerSessionMock.Object))
                {
                    Assert.Same(innerSessionMock.Object, DbSessionHandler.TryGetCurrentSession(engineMock.Object));
                }

                Assert.Same(sessionMock.Object, DbSessionHandler.TryGetCurrentSession(engineMock.Object));
            }
        }

        [Fact]
        public void MissingExecutionContextResolvesNoSession()
        {
            Assert.Null(AsyncLocalContext.Instance.Items);
            Assert.Null(DbSessionHandler.TryGetCurrentSession(engineMock.Object));
        }
    }
}
