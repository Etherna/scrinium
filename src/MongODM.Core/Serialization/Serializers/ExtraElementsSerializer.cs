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
using System;
using System.Collections.Generic;

namespace Etherna.MongODM.Core.Serialization.Serializers
{
    /// <summary>
    /// Utility serializer used for help into document migration scripts.
    /// </summary>
    public class ExtraElementsSerializer : SerializerBase<object>
    {
        // Fields.
        private readonly IDbContext dbContext;

        // Constructor.
        public ExtraElementsSerializer(IDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        // Methods.
        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, object value)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (value is IDictionary<string, object> dictionary)
            {
                context.Writer.WriteStartDocument();
                foreach (var pair in dictionary)
                {
                    context.Writer.WriteName(pair.Key);
                    Serialize(context, args, pair.Value);
                }
                context.Writer.WriteEndDocument();
            }
            else if (value is IList<object> list)
            {
                context.Writer.WriteStartArray();
                foreach (var element in list)
                {
                    Serialize(context, args, element);
                }
                context.Writer.WriteEndArray();
            }
            else if (value is null)
            {
                context.Writer.WriteNull();
            }
            else
            {
                var serializer = dbContext.SerializerRegistry.GetSerializer(value.GetType());
                serializer.Serialize(context, value);
            }
        }

        public TValue DeserializeValue<TValue>(
            object extraElements,
            IBsonSerializer<TValue>? serializer = null)
        {
            /* 
             * Must create a context container because arrays
             * can't be serialized on root of documents.
             */
            var document = new BsonDocument();
            using var documentWriter = new BsonDocumentWriter(document);
            var serializationContext = BsonSerializationContext.CreateRoot(documentWriter);

            serializationContext.Writer.WriteStartDocument();
            serializationContext.Writer.WriteName("container");
            this.Serialize(serializationContext, extraElements);
            serializationContext.Writer.WriteEndDocument();

            // Lookup for a serializer.
            serializer ??= dbContext.SerializerRegistry.GetSerializer<TValue>();

            // Deserialize.
            using var documentReader = new BsonDocumentReader(document);
            var deserializationContext = BsonDeserializationContext.CreateRoot(documentReader);

            deserializationContext.Reader.ReadStartDocument();
            deserializationContext.Reader.ReadName();
            return serializer.Deserialize(
                deserializationContext,
                new BsonDeserializationArgs { NominalType = typeof(TValue) });
        }
    }
}
