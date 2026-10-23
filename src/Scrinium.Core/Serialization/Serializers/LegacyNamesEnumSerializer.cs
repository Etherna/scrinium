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

using Etherna.MongoDB.Bson;
using Etherna.MongoDB.Bson.Serialization;
using Etherna.MongoDB.Bson.Serialization.Serializers;
using System;
using System.Collections.Generic;

namespace Etherna.Scrinium.Core.Serialization.Serializers
{
    /// <summary>
    /// Serializes an enum by the name of its member, as <see cref="EnumSerializer{TEnum}"/>
    /// with the string representation does, reading also the names some members were stored
    /// with before a rename: the documents written before it keep reading, and the ones
    /// written from then on carry the current names. A legacy name is never written.
    /// </summary>
    /// <typeparam name="TEnum">The enum type</typeparam>
    internal sealed class LegacyNamesEnumSerializer<TEnum> : SerializerBase<TEnum>
        where TEnum : struct, Enum
    {
        // Fields.
        private readonly EnumSerializer<TEnum> currentNamesSerializer = new(BsonType.String);
        private readonly IReadOnlyDictionary<string, TEnum> legacyNames;

        // Constructor.
        /// <param name="legacyNames">The members by the names they were stored with</param>
        public LegacyNamesEnumSerializer(IReadOnlyDictionary<string, TEnum> legacyNames)
        {
            ArgumentNullException.ThrowIfNull(legacyNames);

            this.legacyNames = legacyNames;
        }

        // Methods.
        public override TEnum Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            ArgumentNullException.ThrowIfNull(context);

            /* Only a string can carry a legacy name: read it here, leaving every other
             * representation to the enum serializer. */
            if (context.Reader.GetCurrentBsonType() != BsonType.String)
                return currentNamesSerializer.Deserialize(context, args);

            var name = context.Reader.ReadString();
            return legacyNames.TryGetValue(name, out var value)
                ? value
                : Enum.Parse<TEnum>(name);
        }

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, TEnum value) =>
            currentNamesSerializer.Serialize(context, args, value);
    }
}
