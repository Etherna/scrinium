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

using Etherna.MongODM.Core.ExecContext;
using Etherna.MongODM.Core.Models;
using Etherna.MongODM.Core.Options;
using Etherna.MongODM.Core.ProxyModels;
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.Core.Serialization.Mapping;
using Etherna.MongODM.Core.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Etherna.MongODM.Core.Utility
{
    public class DbMaintainerTest
    {
        // Fields.
        private readonly MemberInfo changedMember = typeof(FakeModel).GetProperty(nameof(FakeModel.StringProp))!;
        private readonly DbMaintainer dbMaintainer;
        private readonly FakeModel updatedModel = new() { Id = "modelId" };

        private readonly Mock<IDbContextEngine> engineMock = new();
        private readonly Mock<IExecutionContext> executionContextMock = new();
        private readonly Mock<IMapRegistry> mapRegistryMock = new();
        private readonly Mock<IDbContextOptions> optionsMock = new();
        private readonly Mock<IProxyGenerator> proxyGeneratorMock = new();
        private readonly Mock<IRepository> referenceRepositoryMock = new();
        private readonly Mock<ITaskRunner> taskRunnerMock = new();

        // Constructor.
        public DbMaintainerTest()
        {
            executionContextMock.Setup(c => c.Items).Returns(new Dictionary<object, object?>());
            optionsMock.Setup(o => o.DbName).Returns("test-db");
            proxyGeneratorMock.Setup(g => g.PurgeProxyType(It.IsAny<Type>())).Returns<Type>(type => type);
            referenceRepositoryMock.Setup(r => r.Name).Returns("fakeModels");

            engineMock.Setup(e => e.DbContextType).Returns(typeof(IDbContext));
            engineMock.Setup(e => e.ExecutionContext).Returns(executionContextMock.Object);
            engineMock.Setup(e => e.MapRegistry).Returns(mapRegistryMock.Object);
            engineMock.Setup(e => e.Options).Returns(optionsMock.Object);
            engineMock.Setup(e => e.ProxyGenerator).Returns(proxyGeneratorMock.Object);

            dbMaintainer = new DbMaintainer(taskRunnerMock.Object);
            dbMaintainer.Initialize(engineMock.Object, new Mock<ILogger>().Object);
        }

        // Tests.
        [Fact]
        public void OnUpdatedModelEnqueuesTaskWithInvolvedReferenceMembers()
        {
            // Setup.
            var idMemberMapMock = new Mock<IMemberMap>();
            idMemberMapMock.Setup(mm => mm.Id).Returns("idMemberMapId");

            var referenceMemberMapMock = new Mock<IMemberMap>();
            referenceMemberMapMock.Setup(mm => mm.IsEntityReferenceMember).Returns(true);
            referenceMemberMapMock.Setup(mm => mm.OwnerEntityIdMap).Returns(idMemberMapMock.Object);

            mapRegistryMock.Setup(r => r.GetMemberMapsFromMemberInfo(changedMember))
                .Returns([referenceMemberMapMock.Object]);
            mapRegistryMock.Setup(r => r.GetMemberMapsWithSameElementPath(idMemberMapMock.Object))
                .Returns([idMemberMapMock.Object]);

            // Action.
            dbMaintainer.OnUpdatedModel<string>(updatedModel, [changedMember], referenceRepositoryMock.Object);

            // Assert.
            taskRunnerMock.Verify(
                r => r.RunUpdateDocDependenciesTask(
                    typeof(IDbContext),
                    "fakeModels",
                    "modelId",
                    It.Is<IEnumerable<string>>(ids => ids.Single() == "idMemberMapId")),
                Times.Once());
        }

        [Fact]
        public void OnUpdatedModelSkipsEnqueueWithoutOwnerEntityIdMaps()
        {
            // Setup: a reference member of a schema without an id of its own resolves no owner id.
            var referenceMemberMapMock = new Mock<IMemberMap>();
            referenceMemberMapMock.Setup(mm => mm.IsEntityReferenceMember).Returns(true);
            referenceMemberMapMock.Setup(mm => mm.OwnerEntityIdMap).Returns((IMemberMap?)null);

            mapRegistryMock.Setup(r => r.GetMemberMapsFromMemberInfo(changedMember))
                .Returns([referenceMemberMapMock.Object]);

            // Action.
            dbMaintainer.OnUpdatedModel<string>(updatedModel, [changedMember], referenceRepositoryMock.Object);

            // Assert.
            VerifyNoEnqueuedTask();
        }

        [Fact]
        public void OnUpdatedModelSkipsEnqueueWithoutReferenceMembers()
        {
            // Setup: the changed member is mapped only by root schemas, out of any reference summary.
            var memberMapMock = new Mock<IMemberMap>();
            memberMapMock.Setup(mm => mm.IsEntityReferenceMember).Returns(false);

            mapRegistryMock.Setup(r => r.GetMemberMapsFromMemberInfo(changedMember))
                .Returns([memberMapMock.Object]);

            // Action.
            dbMaintainer.OnUpdatedModel<string>(updatedModel, [changedMember], referenceRepositoryMock.Object);

            // Assert.
            VerifyNoEnqueuedTask();
        }

        // Helpers.
        private void VerifyNoEnqueuedTask() =>
            taskRunnerMock.Verify(
                r => r.RunUpdateDocDependenciesTask(
                    It.IsAny<Type>(),
                    It.IsAny<string>(),
                    It.IsAny<object>(),
                    It.IsAny<IEnumerable<string>>()),
                Times.Never());
    }
}
