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
using System;

namespace Etherna.Scrinium.Core.FilterDefinition
{
    internal static class IdFilterValueHelper
    {
        /* An entity id is always a value: repositories, identity map and references
         * address a document by an atomic key, so a composite id is refused where it
         * renders, whatever serializer produced it. A document is also the only shape
         * MongoDB can read as an operator expression instead of a value, so refusing it
         * closes the operator injection of a caller controlled id. */
        public static void ThrowIfNotValueShaped(BsonElement renderedEqElement)
        {
            var valueShape = renderedEqElement.Value switch
            {
                BsonDocument => "document",
                BsonArray => "array",
                _ => null
            };

            if (valueShape is not null)
                throw new FormatException(
                    $"The id filter value on element \"{renderedEqElement.Name}\" serializes to a {valueShape}, " +
                    "but an entity id must serialize to a value. Serialize a composite id into a value " +
                    "(a string, for instance), and map its components as members of the model to query them");
        }
    }
}
