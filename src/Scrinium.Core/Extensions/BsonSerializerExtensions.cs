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
using System;
using System.Diagnostics.CodeAnalysis;

namespace Etherna.Scrinium.Core.Extensions
{
    internal static class BsonSerializerExtensions
    {
        // Consts.
        private const string WrapperElementName = "value";

        // Methods.
        /// <summary>
        /// Serialize a value apart from any document.
        /// </summary>
        /// <remarks>
        /// A serializer writes its value as an element of a document, so serializing one apart
        /// needs a wrapper document hosting it: the value is the element written into it.
        /// </remarks>
        /// <param name="serializer">The serializer of the value</param>
        /// <param name="value">The value to serialize</param>
        /// <returns>The serialized value</returns>
        public static BsonValue SerializeToBsonValue(this IBsonSerializer serializer, object? value)
        {
            ArgumentNullException.ThrowIfNull(serializer);

            var wrapper = new BsonDocument();
            using (var bsonWriter = new BsonDocumentWriter(wrapper))
            {
                var context = BsonSerializationContext.CreateRoot(bsonWriter);
                bsonWriter.WriteStartDocument();
                bsonWriter.WriteName(WrapperElementName);
                serializer.Serialize(context, value);
                bsonWriter.WriteEndDocument();
            }
            return wrapper[WrapperElementName];
        }

        /// <summary>
        /// Unwrap a container serializer to the serializer of the values it wraps: the value
        /// serializer of a dictionary, or the item serializer of an array.
        /// </summary>
        /// <remarks>
        /// Several serializers implement the container interfaces also when they can't provide
        /// the required information, so the dictionary is tried first: the array unwrap of a
        /// dictionary would stop on its key value pair serializer, which wraps no value.
        /// </remarks>
        /// <param name="serializer">The serializer to unwrap</param>
        /// <param name="childSerializer">The serializer of the wrapped values</param>
        /// <returns>True if the serializer wraps values into a container</returns>
        public static bool TryGetContainerChildSerializer(
            this IBsonSerializer serializer,
            [MaybeNullWhen(false)] out IBsonSerializer childSerializer)
        {
            ArgumentNullException.ThrowIfNull(serializer);

            if (serializer is IBsonDictionarySerializer dictionarySerializer)
            {
                try
                {
                    childSerializer = dictionarySerializer.ValueSerializer;
                    return true;
                }
                catch { }
            }

            if (serializer is IBsonArraySerializer arraySerializer &&
                arraySerializer.TryGetItemSerializationInfo(out var itemSerializationInfo))
            {
                childSerializer = itemSerializationInfo.Serializer;
                return true;
            }

            childSerializer = null;
            return false;
        }
    }
}
