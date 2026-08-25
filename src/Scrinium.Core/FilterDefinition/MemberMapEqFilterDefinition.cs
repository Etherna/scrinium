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
using Etherna.MongoDB.Driver;
using Etherna.Scrinium.Core.Exceptions;
using Etherna.Scrinium.Core.FieldDefinition;
using Etherna.Scrinium.Core.Serialization.Mapping;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Etherna.Scrinium.Core.FilterDefinition
{
    public class MemberMapEqFilterDefinition<TDocument, TItem> : FilterDefinition<TDocument>
    {
        private const string ElemMatchCommand = "$elemMatch";
        private const string EqCommand = "$eq";

        // Fields.
        private readonly IMemberMap memberMap;
        private readonly TItem value;

        // Constructor.
        public MemberMapEqFilterDefinition(
            IMemberMap memberMap,
            TItem value)
        {
            ArgumentNullException.ThrowIfNull(memberMap);
            
            if (memberMap.ElementPathHasUndefinedDocumentElement)
                throw new ArgumentException("Can't create filter with member map path having undefined document elements");

            this.memberMap = memberMap;
            this.value = value;
        }

        // Methods.
        public override BsonDocument Render(RenderArgs<TDocument> args)
        {
            var memberMapFieldDefinition = new MemberMapFieldDefinition<TDocument, TItem>(
                memberMap,
                _ => $".{ElemMatchCommand}",
                _ => throw new ScriniumElementPathRenderingException());
            var renderedField = memberMapFieldDefinition.Render(args);
            var segmentedField = renderedField.FieldName.Split('.');
            var filterDocument = BuildBsonDocument(segmentedField, value, renderedField.ValueSerializer, memberMap.IsIdMember);
            return filterDocument;
        }

        // Helpers.
        private static BsonDocument BuildBsonDocument(
            IEnumerable<string> segmentedField,
            TItem value,
            IBsonSerializer<TItem> valueSerializer,
            bool isIdMember)
        {
            // Recursion building elemMatch filters.
            var sb = new StringBuilder();
            foreach ( var (fieldSegment, i) in segmentedField.Select((f, i) => (f, i)))
            {
                if (fieldSegment == ElemMatchCommand)
                    return sb.Length == 0 ?
                        new BsonDocument(ElemMatchCommand, BuildBsonDocument(segmentedField.Skip(i + 1), value, valueSerializer, isIdMember)) :
                        new BsonDocument(sb.ToString(), new BsonDocument(ElemMatchCommand, BuildBsonDocument(segmentedField.Skip(i + 1), value, valueSerializer, isIdMember)));
                else
                    sb.Append((sb.Length == 0 ? "" : ".") + fieldSegment);
            }

            // Exit building eq filter.
            var valueDocument = new BsonDocument();
            using (var bsonWriter = new BsonDocumentWriter(valueDocument))
            {
                var context = BsonSerializationContext.CreateRoot(bsonWriter);
                bsonWriter.WriteStartDocument();
                bsonWriter.WriteName(sb.ToString());
                valueSerializer.Serialize(context, value);
                bsonWriter.WriteEndDocument();
            }

            //an id member filters a document by its key, and an entity id is always a value
            if (isIdMember)
                IdFilterValueHelper.ThrowIfNotValueShaped(valueDocument.GetElement(0));

            /* The comparison is explicit: MongoDB reads a filter value document whose first
             * element name starts with "$" as an operator expression, while inside an $eq
             * every value is compared literally. A serializer deriving element names from
             * the serialized value can't turn the equality into another query, whatever the
             * value it receives. */
            return new BsonDocument(sb.ToString(), new BsonDocument(EqCommand, valueDocument[0]));
        }
    }
}
