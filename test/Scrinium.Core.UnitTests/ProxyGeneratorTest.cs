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
using Etherna.MongODM.Core.Models;
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.Core.Serialization.Modifiers;
using Etherna.MongODM.Core.Utility;
using Moq;
using System;
using Xunit;

namespace Etherna.MongODM.Core.ProxyModels
{
    public class ProxyGeneratorTest
    {
        // Fields.
        private readonly Mock<IDbContextEngine> dbContextEngineMock = new();
        private readonly ProxyGenerator proxyGenerator = new(AsyncLocalContext.Instance);

        // Constructor.
        public ProxyGeneratorTest()
        {
            dbContextEngineMock.Setup(e => e.ExecutionContext)
                .Returns(AsyncLocalContext.Instance);
            dbContextEngineMock.Setup(e => e.SerializerModifierAccessor)
                .Returns(new SerializerModifierAccessor(AsyncLocalContext.Instance));
        }

        // Tests.
        [Fact]
        public void CreateInstanceBindsTheSourceRepositoryOfTheOperation()
        {
            /* A proxy binds the repository of the operation materializing it: its origin
             * for saves, lazy loads and the identity map. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var repositoryMock = new Mock<IRepository>();
            using var dbExecutionContext = new DbExecutionContextHandler(
                dbContextEngineMock.Object, repositoryMock.Object);

            // Action.
            var model = proxyGenerator.CreateInstance<FakeModel>();

            // Assert.
            Assert.Same(repositoryMock.Object, ((IReferenceable)model).SourceRepository);
        }

        [Fact]
        public void CreateInstanceWithoutRepositoryThrows()
        {
            /* Every proxy materializes inside an operation addressing a collection: without
             * a source repository the instance couldn't save nor lazy load, so the creation
             * fails loudly instead of returning a crippled model. */

            // Setup.
            //an engine level handler, like schema registration: no repository on the flow
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            using var dbExecutionContext = new DbExecutionContextHandler(dbContextEngineMock.Object);

            // Action & assert.
            var exception = Assert.Throws<InvalidOperationException>(
                () => proxyGenerator.CreateInstance<FakeModel>());
            Assert.Contains(nameof(FakeModel), exception.Message, StringComparison.Ordinal);
            Assert.Contains("repository", exception.Message, StringComparison.Ordinal);
        }
    }
}
