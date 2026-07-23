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

using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.ExecContext.AsyncLocal;
using Etherna.MongODM.Core.MockHelpers;
using Etherna.MongODM.Core.Models;
using Etherna.MongODM.Core.ProxyModels;
using Etherna.MongODM.Core.Serialization.Modifiers;
using Etherna.MongODM.Core.Utility;
using Moq;
using Xunit;

namespace Etherna.MongODM.Core
{
    public class ChangeTrackingInterceptorTest
    {
        // Fields.
        private readonly Mock<IDbContext> dbContextMock;
        private readonly Mock<IDbContextEngine> dbContextEngineMock;
        private readonly Mock<ISerializerModifierAccessor> serializerModifierAccessorMock;

        // Constructor.
        public ChangeTrackingInterceptorTest()
        {
            serializerModifierAccessorMock = new Mock<ISerializerModifierAccessor>();

            dbContextEngineMock = new Mock<IDbContextEngine>();
            dbContextEngineMock.Setup(e => e.ExecutionContext)
                .Returns(AsyncLocalContext.Instance);
            dbContextEngineMock.Setup(e => e.SerializerModifierAccessor)
                .Returns(serializerModifierAccessorMock.Object);

            dbContextMock = new Mock<IDbContext>();
            dbContextMock.Setup(c => c.Engine)
                .Returns(dbContextEngineMock.Object);
        }

        // Tests.
        [Fact]
        public void GetDoesNotMarkModelAsChangeCandidate()
        {
            // Setup.
            var model = new FakeModel { Id = "id" };
            var interceptor = CreateBoundInterceptor();

            // Action.
            interceptor.Intercept(InterceptorMockHelper.GetPropertyGetInvocationMock<FakeModel, int>(
                m => m.IntegerProp, model).Object);

            // Assert.
            dbContextMock.Verify(c => c.MarkChangeCandidate(It.IsAny<IEntityModel>()), Times.Never());
        }

        [Fact]
        public void NoCacheModifierSkipsTracking()
        {
            // Setup.
            serializerModifierAccessorMock.Setup(a => a.IsNoCacheEnabled)
                .Returns(true);

            var model = new FakeModel { Id = "id" };
            var interceptor = CreateBoundInterceptor();

            // Action.
            interceptor.Intercept(InterceptorMockHelper.GetPropertySetInvocationMock<FakeModel, int>(
                m => m.IntegerProp, 42, model).Object);

            // Assert.
            dbContextMock.Verify(c => c.MarkChangeCandidate(It.IsAny<IEntityModel>()), Times.Never());
        }

        [Fact]
        public void SetMarksModelAsChangeCandidate()
        {
            // Setup.
            var model = new FakeModel { Id = "id" };
            var interceptor = CreateBoundInterceptor();

            // Action.
            interceptor.Intercept(InterceptorMockHelper.GetPropertySetInvocationMock<FakeModel, int>(
                m => m.IntegerProp, 42, model).Object);

            // Assert.
            dbContextMock.Verify(c => c.MarkChangeCandidate(model), Times.Once());
        }

        [Fact]
        public void UnboundInterceptorSkipsTracking()
        {
            // Setup.
            //no db execution context handler pushed: like a model created outside of a scope
            using var asyncLocalContext = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var interceptor = new ChangeTrackingInterceptor<FakeModel>(
                [],
                dbContextEngineMock.Object);

            var model = new FakeModel { Id = "id" };

            // Action.
            interceptor.Intercept(InterceptorMockHelper.GetPropertySetInvocationMock<FakeModel, int>(
                m => m.IntegerProp, 42, model).Object);

            // Assert.
            dbContextMock.Verify(c => c.MarkChangeCandidate(It.IsAny<IEntityModel>()), Times.Never());
        }

        // Helpers.
        private ChangeTrackingInterceptor<FakeModel> CreateBoundInterceptor()
        {
            /* Create the interceptor inside a db context scope handler, like during
             * a model deserialization inside a repository call. */
            using var dbExecutionContext = new DbExecutionContextHandler(dbContextMock.Object);
            return new ChangeTrackingInterceptor<FakeModel>(
                [],
                dbContextEngineMock.Object);
        }
    }
}
