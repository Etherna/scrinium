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
using System;

namespace Etherna.Scrinium.Core.Serialization.Serializers
{
    public class HexToBinaryDataSerializer : SerializerBase<string>
    {
        // Methods.
        public override string Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            ArgumentNullException.ThrowIfNull(context);

            var bsonReader = context.Reader;
            var bsonType = bsonReader.GetCurrentBsonType();
            switch (bsonType)
            {
                case BsonType.Binary:
                    break;
                case BsonType.Null:
                    bsonReader.ReadNull();
                    return null!;
                default:
                    var message = $"Expected a value of type Binary, but found a value of type {bsonType} instead.";
                    throw new InvalidOperationException(message);
            }

            var binaryData = bsonReader.ReadBinaryData();
            return BsonUtils.ToHexString(binaryData.Bytes);
        }

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, string value)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (value == null)
            {
                context.Writer.WriteNull();
                return;
            }

            var binaryData = new BsonBinaryData(BsonUtils.ParseHexString(value));
            context.Writer.WriteBinaryData(binaryData);
        }
    }
}
