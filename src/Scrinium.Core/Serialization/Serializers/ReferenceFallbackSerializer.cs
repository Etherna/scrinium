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
using Etherna.MongODM.Core.Serialization.Mapping;
using System;

namespace Etherna.MongODM.Core.Serialization.Serializers
{
    /// <summary>
    /// Default fallback deserializer for reference documents with an unrecognized model map schema id.
    /// Because the document is only a reference, its id is the only required information: read it with
    /// the active schema, ignoring any other element. The deserialized model is a summary, so skipped
    /// members can lazy load from the origin document. Without a mapped id member the conventional
    /// "_id" element name is tried; a document also missing that element deserializes empty, since a
    /// reference without id can't be resolved anyway.
    /// </summary>
    internal sealed class ReferenceFallbackSerializer(
        IModelMap modelMap,
        string? idElementName)
        : IBsonSerializer
    {
        // Consts.
        public const string DefaultIdElementName = "_id";

        // Properties.
        public Type ValueType => modelMap.ModelType;

        // Methods.
        public object Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            ArgumentNullException.ThrowIfNull(context);

            // Strip the document down to its sole id element.
            var bsonDocument = BsonDocumentSerializer.Instance.Deserialize(context);
            var idOnlyDocument = new BsonDocument();
            if (bsonDocument.TryGetElement(idElementName ?? DefaultIdElementName, out var idElement))
                idOnlyDocument.Add(idElement);

            // Deserialize with the active schema, forcing the concrete model type as nominal.
            using var bsonReader = new BsonDocumentReader(idOnlyDocument);
            var localContext = BsonDeserializationContext.CreateRoot(bsonReader);
            return modelMap.ActiveSchema.Serializer.Deserialize(
                localContext,
                new BsonDeserializationArgs { NominalType = modelMap.ModelType });
        }

        public void Serialize(BsonSerializationContext context, BsonSerializationArgs args, object value) =>
            throw new NotSupportedException("Reference fallback serializer can only deserialize");
    }
}
