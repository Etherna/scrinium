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

using Etherna.MongoDB.Bson;
using Etherna.MongoDB.Bson.Serialization;
using Etherna.MongoDB.Bson.Serialization.Serializers;
using Etherna.MongoDB.Driver;
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Domain.Models.DbMigrationOpAgg;
using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Etherna.Scrinium.Core.Models;
using Etherna.Scrinium.Core.Options;
using Etherna.Scrinium.Core.ProxyModels;
using Etherna.Scrinium.Core.Repositories;
using Etherna.Scrinium.Core.Serialization.Mapping;
using Etherna.Scrinium.Core.Utility;
using Moq;
using System;
using System.Linq;
using Xunit;

namespace Etherna.Scrinium.Core.Domain.ModelMaps
{
    public class DbMigrationOperationMapTest : IDisposable
    {
        // Fields.
        private readonly IDbContextEngine engine;

        // Constructor.
        /* Build a real engine over a real map registry: the internal model maps
         * registered by the engine initialization are the object under test. */
        public DbMigrationOperationMapTest()
        {
            var dependenciesMock = new Mock<IDbDependencies>();
            dependenciesMock.Setup(d => d.BsonSerializerRegistry)
                .Returns(new BsonSerializerRegistry());
            dependenciesMock.Setup(d => d.DbMaintainer)
                .Returns(new Mock<IDbMaintainer>().Object);
            dependenciesMock.Setup(d => d.DbMigrationManager)
                .Returns(new Mock<IDbMigrationManager>().Object);
            dependenciesMock.Setup(d => d.DiscriminatorRegistry)
                .Returns(new Mock<IDiscriminatorRegistry>().Object);
            dependenciesMock.Setup(d => d.ExecutionContext)
                .Returns(AsyncLocalContext.Instance);
            dependenciesMock.Setup(d => d.MapRegistry)
                .Returns(new MapRegistry());
            var proxyGeneratorMock = new Mock<IProxyGenerator>();
            proxyGeneratorMock.Setup(p => p.PurgeProxyType(It.IsAny<Type>()))
                .Returns<Type>(t => t);
            dependenciesMock.Setup(d => d.ProxyGenerator)
                .Returns(proxyGeneratorMock.Object);
            dependenciesMock.Setup(d => d.RepositoryRegistry)
                .Returns(new RepositoryRegistry());

            var mongoClientMock = new Mock<IMongoClient>();
            mongoClientMock.Setup(c => c.GetDatabase(It.IsAny<string>(), It.IsAny<MongoDatabaseSettings>()))
                .Returns(new Mock<IMongoDatabase>().Object);

            engine = new FakeDbContext().BuildEngine(
                dependenciesMock.Object,
                mongoClientMock.Object,
                new DbContextOptions());
        }

        // Dispose.
        public void Dispose()
        {
            (engine as IDisposable)?.Dispose();
            GC.SuppressFinalize(this);
        }

        // Tests.
        [Fact]
        public void CompletedDateTimePersistsAsBsonDateTime()
        {
            // Action.
            var memberMap = engine.MapRegistry.GetModelMap(typeof(DbMigrationOperation))
                .ActiveSchema.AllMemberMaps
                .Single(mm => mm.MemberName == nameof(DbMigrationOperation.CompletedDateTime));

            // Assert.
            var serializer = Assert.IsType<NullableSerializer<DateTimeOffset>>(memberMap.GetSerializer());
            var valueSerializer = Assert.IsType<DateTimeOffsetSerializer>(
                ((IChildSerializerConfigurable)serializer).ChildSerializer);
            Assert.Equal(BsonType.DateTime, valueSerializer.Representation);
        }

        [Fact]
        public void MigrationLogCreationDateTimePersistsAsBsonDateTime()
        {
            // Action.
            var memberMap = engine.MapRegistry.GetModelMap(typeof(MigrationLogBase))
                .ActiveSchema.AllMemberMaps
                .Single(mm => mm.MemberName == nameof(MigrationLogBase.CreationDateTime));

            // Assert.
            var serializer = Assert.IsType<DateTimeOffsetSerializer>(memberMap.GetSerializer());
            Assert.Equal(BsonType.DateTime, serializer.Representation);
        }
    }
}
