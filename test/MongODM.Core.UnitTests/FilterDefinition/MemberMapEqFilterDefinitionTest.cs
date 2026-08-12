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
using Etherna.MongoDB.Bson.Serialization.Serializers;
using Etherna.MongoDB.Driver;
using Etherna.MongODM.Core.Models;
using Etherna.MongODM.Core.Serialization.Mapping;
using Moq;
using System;
using Xunit;

namespace Etherna.MongODM.Core.FilterDefinition
{
    public class MemberMapEqFilterDefinitionTest
    {
        // Internal classes.
        /* A custom serializer emitting a document whose first element name derives from
         * the serialized value: the shape MODM-222 guards against, since a caller
         * controlled name starting with "$" would render as an operator expression. The
         * dependencies update task builds this filter with an object typed value, like
         * the tested one. */
        public sealed class KeyNameDocumentSerializer : SerializerBase<string>
        {
            public override string Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
            {
                context.Reader.ReadStartDocument();
                var name = context.Reader.ReadName();
                context.Reader.ReadInt32();
                context.Reader.ReadEndDocument();
                return name;
            }

            public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, string value)
            {
                context.Writer.WriteStartDocument();
                context.Writer.WriteName(value);
                context.Writer.WriteInt32(1);
                context.Writer.WriteEndDocument();
            }
        }

        // Fields.
        private readonly Mock<IMemberMap> memberMapMock = new();
        private readonly BsonSerializerRegistry serializerRegistry = new();

        // Constructor.
        public MemberMapEqFilterDefinitionTest()
        {
            serializerRegistry.RegisterSerializationProvider(new PrimitiveSerializationProvider());
            serializerRegistry.RegisterSerializationProvider(new BsonObjectModelSerializationProvider());

            var engineMock = new Mock<IDbContextEngine>();
            engineMock.Setup(e => e.SerializerRegistry)
                .Returns(serializerRegistry);

            memberMapMock.Setup(mm => mm.DbContextEngine)
                .Returns(engineMock.Object);
            memberMapMock.Setup(mm => mm.ElementPathHasUndefinedDocumentElement)
                .Returns(false);
            memberMapMock.Setup(mm => mm.RenderElementPath(
                    false,
                    It.IsAny<Func<ArrayElementRepresentation, string>>(),
                    It.IsAny<Func<DocumentElementRepresentation, string>>()))
                .Returns("Child._id");
            memberMapMock.Setup(mm => mm.Serializer)
                .Returns(new KeyNameDocumentSerializer());
        }

        // Tests.
        [Theory]
        [InlineData("code")]
        [InlineData("$ne")]
        public void RenderComparesADocumentSerializedValueLiterally(string keyName)
        {
            /* MODM-222: any member but an id compares whatever it holds, a document
             * included. The comparison is explicit, so a value serialized to a document
             * whose first element name starts with "$" — the shape MongoDB would read as
             * an operator expression — compares like any other value. */

            // Setup.
            memberMapMock.Setup(mm => mm.IsIdMember)
                .Returns(false);
            var filter = new MemberMapEqFilterDefinition<FakeModel, object>(memberMapMock.Object, keyName);

            // Action.
            var rendered = filter.Render(BuildRenderArgs());

            // Assert.
            Assert.Equal(
                new BsonDocument("Child._id", new BsonDocument("$eq", new BsonDocument(keyName, 1))),
                rendered);
        }

        [Fact]
        public void RenderRefusesACompositeIdMemberValue()
        {
            /* MODM-222: an entity id is always a value, on the reference id paths the
             * dependencies update task filters by, like on the repository id filters. */

            // Setup.
            memberMapMock.Setup(mm => mm.IsIdMember)
                .Returns(true);
            var filter = new MemberMapEqFilterDefinition<FakeModel, object>(memberMapMock.Object, "code");

            // Action.
            var exception = Assert.Throws<FormatException>(() => filter.Render(BuildRenderArgs()));

            // Assert.
            Assert.Contains("Child._id", exception.Message, StringComparison.Ordinal);
            Assert.Contains("must serialize to a value", exception.Message, StringComparison.Ordinal);
        }

        // Helpers.
        private RenderArgs<FakeModel> BuildRenderArgs() =>
            new(new Mock<IBsonSerializer<FakeModel>>().Object, serializerRegistry);
    }
}
