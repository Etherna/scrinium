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
using Etherna.MongODM.Core.Conventions;
using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.ExecContext.AsyncLocal;
using Etherna.MongODM.Core.Models;
using Etherna.MongODM.Core.Options;
using Etherna.MongODM.Core.Serialization.Mapping;
using Etherna.MongODM.Core.Serialization.Serializers;
using Moq;
using System;
using System.Reflection;
using Xunit;

namespace Etherna.MongODM.Core
{
    public class ReferenceSerializerTest
    {
        // Fields.
        private readonly Mock<IDbContextEngine> dbContextEngineMock = new();
        private readonly Mock<IDiscriminatorRegistry> discriminatorRegistryMock = new();

        // Constructor.
        public ReferenceSerializerTest()
        {
            discriminatorRegistryMock.Setup(r => r.LookupDiscriminatorConvention(It.IsAny<Type>()))
                .Returns(() => new HierarchicalProxyTolerantDiscriminatorConvention(dbContextEngineMock.Object, "_t"));

            dbContextEngineMock.Setup(e => e.DiscriminatorRegistry)
                .Returns(() => discriminatorRegistryMock.Object);
            dbContextEngineMock.Setup(e => e.ExecutionContext)
                .Returns(AsyncLocalContext.Instance);
            dbContextEngineMock.Setup(e => e.MapRegistry)
                .Returns(new MapRegistry());
            dbContextEngineMock.Setup(e => e.Options.ModelMapSchemaId)
                .Returns(new ModelMapSchemaIdOptions());
            dbContextEngineMock.Setup(e => e.ProxyGenerator.CreateInstance(It.IsAny<Type>(), It.IsAny<object[]>()))
                .Returns<Type, object[]>((type, arguments) => Activator.CreateInstance(
                    type,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    arguments,
                    null)!);
            dbContextEngineMock.Setup(e => e.ProxyGenerator.IsProxyType(It.IsAny<Type>()))
                .Returns(false);
        }

        // Tests.
        [Fact]
        public void DeserializeClearsExtraElementsOnLoadedSummary()
        {
            /* MODM-3: members can be removed from a reference schema without changing its id
             * (the document model tolerates added and removed fields), so a document written
             * when the member was still serialized resolves the schema, landing the unmapped
             * element in the extra elements bag. The bag is emptied after the load: extra
             * data is never needed with references. */

            // Setup.
            var serializer = new ReferenceSerializer<FakeModel, string>(dbContextEngineMock.Object, config =>
            {
                //the auto mapped ModelBase map carries the extra elements member, as in real configurations
                config.AddModelMap<ModelBase>("modelBaseSchemaId");
                config.AddModelMap<FakeEntityModelBase<string>>("baseSchemaId", mm => mm.MapIdMember(m => m.Id));
                config.AddModelMap<FakeModel>("activeSchemaId", mm => mm.AutoMap());
            });
            var document = new BsonDocument
            {
                { "_s", "activeSchemaId" },
                { "_id", "idVal" },
                { "StringProp", "ok" },
                { "removedProp", "extraVal" }
            };
            var bsonReader = new BsonDocumentReader(document);

            // Action.
            var result = serializer.Deserialize(
                BsonDeserializationContext.CreateRoot(bsonReader),
                new BsonDeserializationArgs { NominalType = typeof(FakeModel) });

            // Assert.
            Assert.Equal("idVal", result.Id);
            Assert.Equal("ok", result.StringProp);
            //an instantiated empty bag proves the unmapped element landed there before the clear
            Assert.NotNull(result.ExtraElements);
            Assert.Empty(result.ExtraElements);
        }
    }
}
