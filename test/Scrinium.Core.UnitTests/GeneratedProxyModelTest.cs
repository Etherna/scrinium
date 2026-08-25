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

using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Exceptions;
using Etherna.Scrinium.Core.Models;
using Etherna.Scrinium.Core.Options;
using Etherna.Scrinium.Core.ProxyModels;
using Etherna.Scrinium.Core.Repositories;
using Moq;
using System;
using System.Threading;
using Xunit;

namespace Etherna.Scrinium.Core
{
    public class GeneratedProxyModelTest
    {
        // Fields.
        private readonly Mock<IDbContext> dbContextMock = new();
        private readonly FakeModelProxy proxyModel = new();
        private readonly Mock<IProxyModelsDbContext> proxyModelsDbContextMock;
        private readonly Mock<IRepository> repositoryMock = new();

        // Constructor.
        public GeneratedProxyModelTest()
        {
            //the proxies invoke the db context through its proxy models surface
            proxyModelsDbContextMock = dbContextMock.As<IProxyModelsDbContext>();
            proxyModelsDbContextMock.Setup(c => c.SuppressChangeTracking())
                .Returns(Mock.Of<IDisposable>());
            repositoryMock.Setup(r => r.DbContext)
                .Returns(dbContextMock.Object);

            ((IProxyModel)proxyModel).BindProxy(dbContextMock.Object, repositoryMock.Object);
        }

        // Tests.
        [Fact]
        public void FullLoadDeniedForMissingOriginDocumentKeepsTheSummaryState()
        {
            // Setup.
            repositoryMock.Setup(r => r.TryFindOneAsync("idVal", It.IsAny<CancellationToken>()))
                .ReturnsAsync((object?)null);
            proxyModelsDbContextMock.Setup(c => c.OnMissingOriginDocument(It.IsAny<IEntityModel>()))
                .Throws(new ScriniumMissingOriginDocumentException());

            proxyModel.Id = "idVal";
            ((IReferenceable)proxyModel).SetAsSummary([], ReactionMode.Throw);

            // Action and assert.
            Assert.Throws<ScriniumMissingOriginDocumentException>(() => proxyModel.StringProp);
            //the denied load never gave up the summary state: the model keeps requiring its origin document
            Assert.True(((IReferenceable)proxyModel).IsSummary);
        }

        [Fact]
        public void FullLoadWithoutOriginDocumentReportsToDbContext()
        {
            // Setup.
            repositoryMock.Setup(r => r.TryFindOneAsync("idVal", It.IsAny<CancellationToken>()))
                .ReturnsAsync((object?)null);

            proxyModel.Id = "idVal";
            ((IReferenceable)proxyModel).SetAsSummary([], ReactionMode.Silent);

            // Action.
            var value = proxyModel.StringProp;

            // Assert.
            //the db context reacts to the db inconsistency, tolerating it here: nothing to load anymore
            proxyModelsDbContextMock.Verify(c => c.OnMissingOriginDocument(proxyModel), Times.Once());
            Assert.Null(value);
            Assert.False(((IReferenceable)proxyModel).IsSummary);
        }

        [Fact]
        public void GetOfLoadedMemberOnSummaryModelDoesntLoad()
        {
            // Setup.
            proxyModel.Id = "idVal";
            proxyModel.StringProp = "loaded";
            ((IReferenceable)proxyModel).SetAsSummary(["StringProp"], ReactionMode.Throw);

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
            ((IReferenceable)proxyModel).SetAsSummary([], ReactionMode.Throw);

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
            proxyModelsDbContextMock.Verify(c => c.MarkChangeCandidate(It.IsAny<IEntityModel>()), Times.Never());
        }

        [Fact]
        public void IdIsImplicitlyLoadedOnSummaries()
        {
            // Setup.
            proxyModel.Id = "idVal";
            ((IReferenceable)proxyModel).SetAsSummary([], ReactionMode.Throw);

            // Action.
            var id = proxyModel.Id;

            // Assert.
            //identity is definitionally present: reading it never loads the full document
            Assert.Equal("idVal", id);
            Assert.True(((IReferenceable)proxyModel).IsSummary);
            repositoryMock.Verify(
                r => r.TryFindOneAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()),
                Times.Never());
        }

        [Fact]
        public void IdStaysReadableOnOutdatedModel()
        {
            // Setup.
            proxyModel.Id = "idVal";
            ((IProxyModel)proxyModel).SetOutdatedModelType(typeof(EvolvedFakeModel));

            // Action.
            var id = proxyModel.Id;

            // Assert.
            //the id survived the document type change, and drives the model reload
            Assert.Equal("idVal", id);
        }

        [Fact]
        public void MergeFullModelOfAnotherTypeIsSkipped()
        {
            // Setup.
            proxyModel.Id = "idVal";
            proxyModel.StringProp = "current";
            ((IReferenceable)proxyModel).SetAsSummary(["StringProp"], ReactionMode.Throw);

            // Action.
            ((IReferenceable)proxyModel).MergeFullModel(new object());

            // Assert.
            //a full model of another type can't merge: the summary state is preserved
            Assert.True(((IReferenceable)proxyModel).IsSummary);
            Assert.Equal("current", proxyModel.StringProp);
        }

        [Fact]
        public void MergeSummaryModelCopiesOnlyMissingMembers()
        {
            // Setup.
            proxyModel.Id = "idVal";
            proxyModel.StringProp = "current";
            ((IReferenceable)proxyModel).SetAsSummary(["StringProp"], ReactionMode.Throw);

            var otherSummaryModel = new FakeModelProxy
            {
                Id = "idVal",
                IntegerProp = 42,
                StringProp = "other"
            };
            ((IReferenceable)otherSummaryModel).SetAsSummary(["IntegerProp", "StringProp"], ReactionMode.Throw);

            // Action.
            ((IReferenceable)proxyModel).MergeSummaryModel(otherSummaryModel);

            // Assert.
            //an already loaded member is never overwritten; a missing one is copied
            Assert.Equal("current", proxyModel.StringProp);
            Assert.Equal(42, proxyModel.IntegerProp);
            Assert.True(((IReferenceable)proxyModel).IsSummary);
        }

        [Theory]
        [InlineData(ReactionMode.Silent, ReactionMode.Warn, ReactionMode.Warn)]
        [InlineData(ReactionMode.Throw, ReactionMode.Silent, ReactionMode.Throw)]
        [InlineData(ReactionMode.Warn, ReactionMode.Throw, ReactionMode.Throw)]
        public void MergeSummaryModelKeepsTheStrictestReactionMode(
            ReactionMode currentMode,
            ReactionMode otherMode,
            ReactionMode expectedMode)
        {
            /* One document materializes one single instance, whatever the references reaching
             * it: the modes they declare can differ, and the instance keeps the strictest. */

            // Setup.
            proxyModel.Id = "idVal";
            ((IReferenceable)proxyModel).SetAsSummary([], currentMode);

            var otherSummaryModel = new FakeModelProxy { Id = "idVal" };
            ((IProxyModel)otherSummaryModel).BindProxy(dbContextMock.Object, repositoryMock.Object);
            ((IReferenceable)otherSummaryModel).SetAsSummary([], otherMode);

            // Action.
            ((IReferenceable)proxyModel).MergeSummaryModel(otherSummaryModel);

            // Assert.
            Assert.Equal(expectedMode, ((IReferenceable)proxyModel).MissingOriginDocument);
        }

        [Fact]
        public void OutdatedModelAllowsInternalReadsUnderSuppression()
        {
            // Setup.
            proxyModel.Id = "idVal";
            proxyModel.StringProp = "value";
            ((IProxyModel)proxyModel).SetOutdatedModelType(typeof(EvolvedFakeModel));
            proxyModelsDbContextMock.Setup(c => c.IsChangeTrackingSuppressed)
                .Returns(true);

            // Action.
            var value = proxyModel.StringProp;

            // Assert.
            Assert.Equal("value", value);
        }

        [Fact]
        public void OutdatedModelDeniesApplicationInteractions()
        {
            // Setup.
            proxyModel.Id = "idVal";
            proxyModel.StringProp = "value";
            ((IProxyModel)proxyModel).SetOutdatedModelType(typeof(EvolvedFakeModel));

            // Assert.
            Assert.Equal(typeof(EvolvedFakeModel), ((IProxyModel)proxyModel).OutdatedModelType);

            var getException = Assert.Throws<ScriniumOutdatedModelTypeException>(() => proxyModel.StringProp);
            Assert.Contains("idVal", getException.Message, StringComparison.Ordinal);
            Assert.Contains($"loaded as type {nameof(FakeModel)}", getException.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(EvolvedFakeModel), getException.Message, StringComparison.Ordinal);

            Assert.Throws<ScriniumOutdatedModelTypeException>(() => proxyModel.StringProp = "other");
        }

        [Fact]
        public void SetOfTheIdMemberIsNotTracked()
        {
            // Action.
            proxyModel.Id = "idVal";

            // Assert.
            //the id member is not proxied: identity never joins the tracking bookkeeping
            Assert.DoesNotContain("Id", ((IReferenceable)proxyModel).SettedMemberNames);
            proxyModelsDbContextMock.Verify(c => c.MarkChangeCandidate(It.IsAny<IEntityModel>()), Times.Never());
        }

        [Fact]
        public void SetRecordsTheMemberAndMarksChangeCandidate()
        {
            // Action.
            proxyModel.StringProp = "value";

            // Assert.
            Assert.Contains("StringProp", ((IReferenceable)proxyModel).SettedMemberNames);
            proxyModelsDbContextMock.Verify(c => c.MarkChangeCandidate(proxyModel), Times.Once());
        }

        // Nested types.
        private sealed class EvolvedFakeModel : FakeModel
        { }
    }
}
