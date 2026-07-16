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
using Etherna.MongODM.Core.MockHelpers;
using Etherna.MongODM.Core.Models;
using Etherna.MongODM.Core.ProxyModels;
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.Core.Utility;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.MongODM.Core
{
    public class ReferenceableInterceptorTest
    {
        private readonly ReferenceableInterceptor<FakeModel, string> interceptor;
        private readonly Mock<IRepository<FakeModel, string>> repositoryMock;
        private readonly Mock<IDbContext> dbContextMock;
        private readonly Mock<IDbContextEngine> dbContextEngineMock;

        private readonly Mock<Castle.DynamicProxy.IInvocation> getIsSummaryInvocationMock;
        private readonly Mock<Castle.DynamicProxy.IInvocation> getLoadedMembersInvocationMock;

        public ReferenceableInterceptorTest()
        {
            repositoryMock = new Mock<IRepository<FakeModel, string>>();

            dbContextEngineMock = new Mock<IDbContextEngine>();
            dbContextEngineMock.Setup(e => e.ExecutionContext)
                .Returns(AsyncLocalContext.Instance);

            dbContextMock = new Mock<IDbContext>();
            dbContextMock.Setup(c => c.Engine)
                .Returns(() => dbContextEngineMock.Object);
            dbContextMock.Setup(c => c.RepositoryRegistry.TryGetRepositoryByHandledModelType(typeof(FakeModel)))
                .Returns(() => repositoryMock.Object);

            var loggerMock = new Mock<ILogger<ReferenceableInterceptor<FakeModel, string>>>();

            /* Resolve the repository binding from the current db context scope,
             * like during a model deserialization inside a repository call. */
            using var dbExecutionContext = new DbExecutionContextHandler(dbContextMock.Object);
            interceptor = new ReferenceableInterceptor<FakeModel, string>(
                [typeof(IReferenceable)],
                dbContextEngineMock.Object,
                loggerMock.Object);

            getIsSummaryInvocationMock = InterceptorMockHelper.GetExternalPropertyGetInvocationMock<FakeModel, IReferenceable, bool>(
                s => s.IsSummary);
            getLoadedMembersInvocationMock = InterceptorMockHelper.GetExternalPropertyGetInvocationMock<FakeModel, IReferenceable, IEnumerable<string>>(
                s => s.SettedMemberNames);
        }

        [Fact]
        public void InitializeAsSummary()
        {
            // Setup.
            var initializeInvocationMock = InterceptorMockHelper.GetExternalMethodInvocationMock<FakeModel, IReferenceable>(
                nameof(IReferenceable.SetAsSummary),
                [ new [] { nameof(FakeModel.Id) } ]);

            // Assert.
            interceptor.Intercept(getIsSummaryInvocationMock.Object);
            interceptor.Intercept(getLoadedMembersInvocationMock.Object);
            Assert.False((bool)getIsSummaryInvocationMock.Object.ReturnValue!);
            Assert.Empty((IEnumerable<string>)getLoadedMembersInvocationMock.Object.ReturnValue!);

            // Action.
            interceptor.Intercept(initializeInvocationMock.Object);

            // Assert.
            interceptor.Intercept(getIsSummaryInvocationMock.Object);
            interceptor.Intercept(getLoadedMembersInvocationMock.Object);
            Assert.True((bool)getIsSummaryInvocationMock.Object.ReturnValue);
            Assert.Equal(
                [nameof(FakeModel.Id)],
                (IEnumerable<string>)getLoadedMembersInvocationMock.Object.ReturnValue!);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void GetLoadedMember(bool isSummary)
        {
            // Setup.
            if (isSummary)
            {
                var initializeInvocationMock = InterceptorMockHelper.GetExternalMethodInvocationMock<FakeModel, IReferenceable>(
                    nameof(IReferenceable.SetAsSummary),
                    [ new[] { nameof(FakeModel.IntegerProp) } ]);
                interceptor.Intercept(initializeInvocationMock.Object);
            }

            var getPropertyInvocationMock = InterceptorMockHelper.GetPropertyGetInvocationMock<FakeModel, int>(
                m => m.IntegerProp);

            // Action.
            interceptor.Intercept(getPropertyInvocationMock.Object);

            // Assert.
            getPropertyInvocationMock.Verify(i => i.Proceed(), Times.Once());
            repositoryMock.Verify(r => r.TryFindOneAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            if (isSummary)
            {
                interceptor.Intercept(getIsSummaryInvocationMock.Object);
                interceptor.Intercept(getLoadedMembersInvocationMock.Object);
                Assert.True((bool)getIsSummaryInvocationMock.Object.ReturnValue!);
                Assert.Equal(
                    [nameof(FakeModel.IntegerProp)],
                    (IEnumerable<string>)getLoadedMembersInvocationMock.Object.ReturnValue!);
            }
            else
            {
                interceptor.Intercept(getIsSummaryInvocationMock.Object);
                interceptor.Intercept(getLoadedMembersInvocationMock.Object);
                Assert.False((bool)getIsSummaryInvocationMock.Object.ReturnValue!);
                Assert.Empty((IEnumerable<string>)getLoadedMembersInvocationMock.Object.ReturnValue!);
            }
        }

        [Fact]
        public void GetNotLoadedMember()
        {
            // Setup.
            var modelId = "ImAnId";
            var stringValue = "LookAtMe";
            var model = new FakeModel
            {
                Id = modelId,
                IntegerProp = 42
            };

            repositoryMock.Setup(r => r.TryFindOneAsync((object)modelId, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<object?>(new FakeModel
                {
                    Id = modelId,
                    IntegerProp = 7,
                    StringProp = stringValue
                }));

            var initializeInvocationMock = InterceptorMockHelper.GetExternalMethodInvocationMock<FakeModel, IReferenceable>(
                nameof(IReferenceable.SetAsSummary),
                [
                    new[]
                    {
                        nameof(FakeModel.Id),
                        nameof(FakeModel.IntegerProp)
                    }
                ]);
            interceptor.Intercept(initializeInvocationMock.Object);

            var getPropertyInvocationMock = InterceptorMockHelper.GetPropertyGetInvocationMock(m => m.StringProp, model);

            // Action.
            interceptor.Intercept(getPropertyInvocationMock.Object);

            // Assert.
            getPropertyInvocationMock.Verify(i => i.Proceed(), Times.Once());
            repositoryMock.Verify(r => r.TryFindOneAsync((object)modelId, It.IsAny<CancellationToken>()), Times.Once);

            interceptor.Intercept(getIsSummaryInvocationMock.Object);
            interceptor.Intercept(getLoadedMembersInvocationMock.Object);
            Assert.False((bool)getIsSummaryInvocationMock.Object.ReturnValue!);
            
            Assert.Equal(modelId, model.Id);
            Assert.Equal(7, model.IntegerProp);
            Assert.Equal(stringValue, model.StringProp);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SetMember(bool isSummary)
        {
            // Setup.
            if (isSummary)
            {
                var initializeInvocationMock = InterceptorMockHelper.GetExternalMethodInvocationMock<FakeModel, IReferenceable>(
                    nameof(IReferenceable.SetAsSummary),
                    [Array.Empty<string>()]);
                interceptor.Intercept(initializeInvocationMock.Object);
            }

            var setPropertyInvocationMock = InterceptorMockHelper.GetPropertySetInvocationMock<FakeModel, int>(
                m => m.IntegerProp, 42);

            // Action.
            interceptor.Intercept(setPropertyInvocationMock.Object);

            // Assert.
            setPropertyInvocationMock.Verify(i => i.Proceed(), Times.Once());
            repositoryMock.Verify(r => r.TryFindOneAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            if (isSummary)
            {
                interceptor.Intercept(getIsSummaryInvocationMock.Object);
                interceptor.Intercept(getLoadedMembersInvocationMock.Object);
                Assert.True((bool)getIsSummaryInvocationMock.Object.ReturnValue!);
                Assert.Equal(
                    [nameof(FakeModel.IntegerProp)],
                    (IEnumerable<string>)getLoadedMembersInvocationMock.Object.ReturnValue!);
            }
            else
            {
                interceptor.Intercept(getIsSummaryInvocationMock.Object);
                interceptor.Intercept(getLoadedMembersInvocationMock.Object);
                Assert.False((bool)getIsSummaryInvocationMock.Object.ReturnValue!);
            }
        }
    }
}
