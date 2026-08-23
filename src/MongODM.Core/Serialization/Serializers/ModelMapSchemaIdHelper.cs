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
using Etherna.MongODM.Core.Serialization.Mapping;
using System;

namespace Etherna.MongODM.Core.Serialization.Serializers
{
    internal static class ModelMapSchemaIdHelper
    {
        /// <summary>
        /// Extract the model map schema id from a deserialized document, removing every
        /// recognized schema id element so it doesn't report into extra elements. The current
        /// element name takes precedence over the deprecated one, recognized on documents
        /// written with a previous element name.
        /// </summary>
        /// <param name="bsonDocument">The deserialized document</param>
        /// <returns>The schema id, or null when no element carries it</returns>
        public static string? ExtractSchemaId(BsonDocument bsonDocument)
        {
            ArgumentNullException.ThrowIfNull(bsonDocument);

            string? schemaId = null;
            var isFound = false;
            string[] elementNames = [ModelMapSchema.IdElementName, ModelMapSchema.DeprecatedIdElementName];
            foreach (var elementName in elementNames)
            {
                if (!bsonDocument.TryGetElement(elementName, out var element))
                    continue;

                if (!isFound)
                {
                    isFound = true;
                    schemaId = element.Value switch
                    {
                        BsonNull _ => null,
                        BsonString bsonString => bsonString.AsString,
                        _ => throw new NotSupportedException(
                            $"Invalid bson type {element.Value.BsonType} for the {elementName} schema id element"),
                    };
                }
                bsonDocument.RemoveElement(element);
            }
            return schemaId;
        }
    }
}
