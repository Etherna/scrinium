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

using Etherna.MongoDB.Driver;
using Etherna.MongODM.Core.ExecContext.AsyncLocal;
using Moq;
using System;
using Xunit;

namespace Etherna.MongODM.Core.Utility
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

        [Fact]
        public void NullArgumentsAreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new DbSessionHandler(null!, sessionMock.Object));
            Assert.Throws<ArgumentNullException>(() => new DbSessionHandler(engineMock.Object, null!));
            Assert.Throws<ArgumentNullException>(() => DbSessionHandler.TryGetCurrentSession(null!));
        }
    }
}
