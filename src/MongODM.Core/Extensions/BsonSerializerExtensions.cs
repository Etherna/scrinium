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

namespace Etherna.MongODM.Core.Extensions
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
    }
}
