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
using Etherna.Scrinium.Core.Domain.Models;
using System;
using System.Collections.Generic;
using Xunit;

namespace Etherna.Scrinium.Core.FilterDefinition
{
    public class EntityIdEqFilterDefinitionTest
    {
        // Internal classes.
        public sealed class ArrayKeyModel : IEntityModel<string[]>
        {
            // Properties.
            public IDictionary<string, object>? ExtraElements { get; }
            public string[] Id { get; set; } = null!;
        }
        public sealed class DocumentKey(string name)
        {
            public string Name { get; } = name;
        }
        public sealed class DocumentKeyModel : IEntityModel<DocumentKey>
        {
            // Properties.
            public IDictionary<string, object>? ExtraElements { get; }
            public DocumentKey Id { get; set; } = null!;
        }
        /* A custom id serializer emitting a document whose first element name derives from
         * the id value: the serialized shape MODM-222 guards against, since a caller
         * controlled name starting with "$" would render as an operator expression. */
        public sealed class DocumentKeySerializer : SerializerBase<DocumentKey>
        {
            public override DocumentKey Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
            {
                context.Reader.ReadStartDocument();
                var name = context.Reader.ReadName();
                context.Reader.ReadInt32();
                context.Reader.ReadEndDocument();
                return new DocumentKey(name);
            }

            public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, DocumentKey value)
            {
                context.Writer.WriteStartDocument();
                context.Writer.WriteName(value.Name);
                context.Writer.WriteInt32(1);
                context.Writer.WriteEndDocument();
            }
        }
        public sealed class StringKeyModel : IEntityModel<string>
        {
            // Properties.
            public IDictionary<string, object>? ExtraElements { get; }
            public string Id { get; set; } = null!;
        }

        // Tests.
        [Fact]
        public void RenderBuildsTheDriverEqFilterOnTheIdMember()
        {
            /* A scalar serialized id can't carry operators: an hostile value stays an
             * inert scalar inside the rendered filter. */

            // Setup.
            var filter = new EntityIdEqFilterDefinition<StringKeyModel, string>("""{"$ne": null}""");

            // Action.
            var rendered = filter.Render(BuildRenderArgs<StringKeyModel>(cm => cm.MapIdMember(m => m.Id)));

            // Assert.
            Assert.Equal(new BsonDocument("_id", """{"$ne": null}"""), rendered);
        }

        [Fact]
        public void RenderRefusesAnArraySerializedIdValue()
        {
            // Setup.
            var filter = new EntityIdEqFilterDefinition<ArrayKeyModel, string[]>(["left", "7"]);

            // Action.
            var exception = Assert.Throws<FormatException>(() => filter.Render(BuildRenderArgs<ArrayKeyModel>(
                cm => cm.MapIdMember(m => m.Id))));

            // Assert.
            Assert.Contains("_id", exception.Message, StringComparison.Ordinal);
            Assert.Contains("array", exception.Message, StringComparison.Ordinal);
            Assert.Contains("must serialize to a value", exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("code")]
        [InlineData("$ne")]
        public void RenderRefusesADocumentSerializedIdValue(string keyName)
        {
            /* MODM-222: an entity id is always a value, and the id serializers emitting a
             * document are the ones the engine build can't see. An hostile value carrying
             * an operator ("$ne") is the reason for the rule: MongoDB would read it as an
             * operator expression, matching an arbitrary document instead of an id. */

            // Setup.
            var filter = new EntityIdEqFilterDefinition<DocumentKeyModel, DocumentKey>(new DocumentKey(keyName));

            // Action.
            var exception = Assert.Throws<FormatException>(() => filter.Render(BuildRenderArgs<DocumentKeyModel>(
                cm => cm.MapIdMember(m => m.Id).SetSerializer(new DocumentKeySerializer()))));

            // Assert.
            Assert.Contains("_id", exception.Message, StringComparison.Ordinal);
            Assert.Contains("document", exception.Message, StringComparison.Ordinal);
            Assert.Contains("must serialize to a value", exception.Message, StringComparison.Ordinal);
        }

        // Helpers.
        private static RenderArgs<TModel> BuildRenderArgs<TModel>(Action<BsonClassMap<TModel>> classMapInitializer)
        {
            var classMap = new BsonClassMap<TModel>(classMapInitializer);
            classMap.Freeze();
            return new(new BsonClassMapSerializer<TModel>(classMap), new BsonSerializerRegistry());
        }
    }
}
