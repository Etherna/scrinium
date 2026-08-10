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
using Etherna.MongoDB.Bson.IO;
using Etherna.MongoDB.Bson.Serialization;
using Etherna.MongoDB.Bson.Serialization.Conventions;
using Etherna.MongODM.Core.ExecContext.AsyncLocal;
using Etherna.MongODM.Core.Serialization.Mapping;
using Etherna.MongODM.Core.Utility;
using Moq;
using System;
using Xunit;

namespace Etherna.MongODM.Core.Conventions
{
    public class HierarchicalProxyTolerantDiscriminatorConventionTest
    {
        // Internal classes.
        private class ForeignModel
        { }

        private sealed class DerivedForeignModel : ForeignModel
        { }

        // Fields.
        private readonly HierarchicalProxyTolerantDiscriminatorConvention convention;
        private readonly Mock<IDbContextEngine> dbContextEngineMock = new();
        private readonly Mock<IDiscriminatorRegistry> discriminatorRegistryMock = new();

        // Constructor.
        public HierarchicalProxyTolerantDiscriminatorConventionTest()
        {
            dbContextEngineMock.Setup(e => e.DiscriminatorRegistry)
                .Returns(() => discriminatorRegistryMock.Object);
            dbContextEngineMock.Setup(e => e.ExecutionContext)
                .Returns(AsyncLocalContext.Instance);

            //the convention statically registered on typeof(object), inherited by every type of the process
            convention = new HierarchicalProxyTolerantDiscriminatorConvention("_t", AsyncLocalContext.Instance);

            //register the class maps of the foreign models on the driver, as any other driver consumer does
            BsonClassMap.LookupClassMap(typeof(DerivedForeignModel));
        }

        // Tests.
        [Fact]
        public void GetActualTypeResolvesWithDriverConventionWithoutAmbientDbContextEngine()
        {
            /* The convention registered on typeof(object) is inherited by every type of the
             * process: outside a db operation it has no engine to resolve types with, and has
             * to behave like the driver convention the type would get without the registration. */

            // Setup.
            var document = new BsonDocument("_t", "DerivedForeignModel");
            using var bsonReader = new BsonDocumentReader(document);
            using var driverBsonReader = new BsonDocumentReader(document);

            // Action.
            var actualType = convention.GetActualType(bsonReader, typeof(ForeignModel));

            // Assert.
            Assert.Equal(typeof(DerivedForeignModel), actualType);
            Assert.Equal(
                StandardDiscriminatorConvention.Hierarchical.GetActualType(driverBsonReader, typeof(ForeignModel)),
                actualType);
        }

        [Fact]
        public void GetActualTypeUsesAmbientDbContextEngine()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            using var dbExecutionContext = new DbExecutionContextHandler(dbContextEngineMock.Object);

            discriminatorRegistryMock.Setup(r => r.IsTypeDiscriminated(typeof(ForeignModel)))
                .Returns(true);
            discriminatorRegistryMock.Setup(r => r.LookupActualType(typeof(ForeignModel), "mongodm"))
                .Returns(typeof(DerivedForeignModel));

            using var bsonReader = new BsonDocumentReader(new BsonDocument("_t", "mongodm"));

            // Action.
            var actualType = convention.GetActualType(bsonReader, typeof(ForeignModel));

            // Assert.
            //the db context engine discriminator registry resolves the type, not the driver one
            Assert.Equal(typeof(DerivedForeignModel), actualType);
            discriminatorRegistryMock.Verify(r => r.LookupActualType(typeof(ForeignModel), "mongodm"), Times.Once);
        }

        [Fact]
        public void GetDiscriminatorResolvesWithDriverConventionWithoutAmbientDbContextEngine()
        {
            // Action.
            var discriminator = convention.GetDiscriminator(typeof(ForeignModel), typeof(DerivedForeignModel));

            // Assert.
            Assert.Equal(
                StandardDiscriminatorConvention.Hierarchical.GetDiscriminator(
                    typeof(ForeignModel), typeof(DerivedForeignModel)),
                discriminator);
            Assert.Equal((BsonValue)"DerivedForeignModel", discriminator);
        }

        [Fact]
        public void GetDiscriminatorResolvesWithDriverObjectConventionForObjectNominalType()
        {
            /* An object typed member resolves with the driver object convention: the type name
             * discriminator, instead of the class map one of the hierarchical convention. */

            // Action.
            var discriminator = convention.GetDiscriminator(typeof(object), typeof(DerivedForeignModel));

            // Assert.
            Assert.Equal(
                ObjectDiscriminatorConvention.Instance.GetDiscriminator(typeof(object), typeof(DerivedForeignModel)),
                discriminator);
        }

        [Fact]
        public void GetDiscriminatorUsesAmbientDbContextEngine()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            using var dbExecutionContext = new DbExecutionContextHandler(dbContextEngineMock.Object);

            var mapRegistryMock = new Mock<IMapRegistry>();
            IModelMap? modelMap = null;
            mapRegistryMock.Setup(r => r.TryGetModelMap(typeof(DerivedForeignModel), out modelMap))
                .Returns(false);
            dbContextEngineMock.Setup(e => e.MapRegistry)
                .Returns(() => mapRegistryMock.Object);
            dbContextEngineMock.Setup(e => e.ProxyGenerator.PurgeProxyType(It.IsAny<Type>()))
                .Returns<Type>(t => t);

            // Action.
            var discriminator = convention.GetDiscriminator(typeof(ForeignModel), typeof(DerivedForeignModel));

            // Assert.
            //an unmapped model has no discriminator on the db context engine, whatever the driver says
            Assert.Null(discriminator);
        }
    }
}
