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
using Etherna.MongODM.Core.Models;
using Etherna.MongODM.Core.ProxyModels;
using Etherna.MongODM.Core.Repositories;
using Moq;
using System;
using System.Threading;
using Xunit;

namespace Etherna.MongODM.Core
{
    public class GeneratedProxyModelTest
    {
        // Fields.
        private readonly Mock<IDbContext> dbContextMock = new();
        private readonly FakeModelProxy proxyModel = new();
        private readonly Mock<IRepository> repositoryMock = new();

        // Constructor.
        public GeneratedProxyModelTest()
        {
            dbContextMock.Setup(c => c.SuppressChangeTracking())
                .Returns(Mock.Of<IDisposable>());
            repositoryMock.Setup(r => r.DbContext)
                .Returns(dbContextMock.Object);

            ((IProxyModel)proxyModel).BindProxy(dbContextMock.Object, repositoryMock.Object);
        }

        // Tests.
        [Fact]
        public void GetOfLoadedMemberOnSummaryModelDoesntLoad()
        {
            // Setup.
            proxyModel.Id = "idVal";
            proxyModel.StringProp = "loaded";
            ((IReferenceable)proxyModel).SetAsSummary(["Id", "StringProp"]);

            // Action.
            var value = proxyModel.StringProp;

            // Assert.
            Assert.Equal("loaded", value);
            Assert.True(((IReferenceable)proxyModel).IsSummary);
            repositoryMock.Verify(
                r => r.TryFindOneAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()),
                Times.Never());
        }

        [Fact]
        public void GetOfNotLoadedMemberOnSummaryModelLoadsTheFullDocument()
        {
            // Setup.
            var fullModel = new FakeModel
            {
                Id = "idVal",
                IntegerProp = 42,
                StringProp = "full"
            };
            repositoryMock.Setup(r => r.TryFindOneAsync("idVal", It.IsAny<CancellationToken>()))
                .ReturnsAsync(fullModel);

            proxyModel.Id = "idVal";
            ((IReferenceable)proxyModel).SetAsSummary(["Id"]);

            // Action.
            var value = proxyModel.StringProp;

            // Assert.
            Assert.Equal("full", value);
            Assert.Equal(42, proxyModel.IntegerProp);
            Assert.False(((IReferenceable)proxyModel).IsSummary);
            repositoryMock.Verify(
                r => r.TryFindOneAsync("idVal", It.IsAny<CancellationToken>()),
                Times.Once());
        }

        [Fact]
        public void GetOfNotMutableMemberDoesntMarkChangeCandidate()
        {
            // Action.
            _ = proxyModel.IntegerProp;

            // Assert.
            dbContextMock.Verify(c => c.MarkChangeCandidate(It.IsAny<IEntityModel>()), Times.Never());
        }

        [Fact]
        public void MergeSummaryModelCopiesOnlyMissingMembers()
        {
            // Setup.
            proxyModel.Id = "idVal";
            proxyModel.StringProp = "current";
            ((IReferenceable)proxyModel).SetAsSummary(["Id", "StringProp"]);

            var otherSummaryModel = new FakeModelProxy
            {
                Id = "idVal",
                IntegerProp = 42,
                StringProp = "other"
            };
            ((IReferenceable)otherSummaryModel).SetAsSummary(["Id", "IntegerProp", "StringProp"]);

            // Action.
            ((IReferenceable)proxyModel).MergeSummaryModel(otherSummaryModel);

            // Assert.
            //an already loaded member is never overwritten; a missing one is copied
            Assert.Equal("current", proxyModel.StringProp);
            Assert.Equal(42, proxyModel.IntegerProp);
            Assert.True(((IReferenceable)proxyModel).IsSummary);
        }

        [Fact]
        public void SetRecordsTheMemberAndMarksChangeCandidate()
        {
            // Action.
            proxyModel.StringProp = "value";

            // Assert.
            Assert.Contains("StringProp", ((IReferenceable)proxyModel).SettedMemberNames);
            dbContextMock.Verify(c => c.MarkChangeCandidate(proxyModel), Times.Once());
        }
    }
}
