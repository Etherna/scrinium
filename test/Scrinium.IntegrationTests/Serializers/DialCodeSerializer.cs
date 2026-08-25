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

using Etherna.MongoDB.Bson.IO;
using Etherna.MongoDB.Bson.Serialization;
using Etherna.MongoDB.Bson.Serialization.Serializers;
using Etherna.Scrinium.IntegrationTests.Models;

namespace Etherna.Scrinium.IntegrationTests.Serializers
{
    public class DialCodeSerializer : SerializerBase<DialCode>
    {
        public override DialCode Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            context.Reader.ReadStartDocument();
            var dial = context.Reader.ReadName();
            var position = context.Reader.ReadInt32();
            context.Reader.ReadEndDocument();
            return new DialCode(dial, position);
        }

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, DialCode value)
        {
            context.Writer.WriteStartDocument();
            context.Writer.WriteName(value.Dial);
            context.Writer.WriteInt32(value.Position);
            context.Writer.WriteEndDocument();
        }
    }
}
