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

using Etherna.MongoDB.Bson.Serialization;
using Etherna.MongoDB.Driver;
using Etherna.MongoDB.Driver.Linq;
using System;
using System.Text;

namespace Etherna.MongODM.Core.FieldDefinition
{
    public class UnmappedFieldDefinition<TDocument> : FieldDefinition<TDocument>
    {
        // Constructor.
        public UnmappedFieldDefinition(
            FieldDefinition<TDocument>? baseDocumentField,
            string unmappedFieldName,
            IBsonSerializer unmappedFieldSerializer)
        {
            ArgumentNullException.ThrowIfNull(unmappedFieldName);
            if (unmappedFieldName.Contains('.', StringComparison.InvariantCulture))
                throw new ArgumentException("Field name can't navigate nested documents", nameof(unmappedFieldName));

            BaseDocumentField = baseDocumentField;
            UnmappedFieldName = unmappedFieldName;
            UnmappedFieldSerializer = unmappedFieldSerializer;
        }

        // Properties.
        public FieldDefinition<TDocument>? BaseDocumentField { get; }
        public string UnmappedFieldName { get; }
        public IBsonSerializer UnmappedFieldSerializer { get; }

        // Methods.
        public override RenderedFieldDefinition Render(RenderArgs<TDocument> args) =>
            new(UnmappedFieldDefinitionHelper.BuildFieldPath(BaseDocumentField, UnmappedFieldName, args.DocumentSerializer, args.SerializerRegistry),
                UnmappedFieldSerializer);

        public Type? TryGetBaseDocumentType(IBsonSerializer<TDocument> documentSerializer, IBsonSerializerRegistry serializerRegistry) =>
            UnmappedFieldDefinitionHelper.TryGetBaseDocumentType(BaseDocumentField, documentSerializer, serializerRegistry);
    }

    public class UnmappedFieldDefinition<TDocument, TField> : FieldDefinition<TDocument, TField>
    {
        // Constructor.
        public UnmappedFieldDefinition(
            FieldDefinition<TDocument>? baseDocumentField,
            string unmappedFieldName,
            IBsonSerializer<TField> unmappedFieldSerializer)
        {
            ArgumentNullException.ThrowIfNull(unmappedFieldName);
            if (unmappedFieldName.Contains('.', StringComparison.InvariantCulture))
                throw new ArgumentException("Field name can't navigate nested documents", nameof(unmappedFieldName));

            BaseDocumentField = baseDocumentField;
            UnmappedFieldName = unmappedFieldName;
            UnmappedFieldSerializer = unmappedFieldSerializer;
        }

        // Properties.
        public FieldDefinition<TDocument>? BaseDocumentField { get; }
        public string UnmappedFieldName { get; }
        public IBsonSerializer<TField> UnmappedFieldSerializer { get; }

        // Methods.
        public override RenderedFieldDefinition<TField> Render(RenderArgs<TDocument> args) =>
            new(UnmappedFieldDefinitionHelper.BuildFieldPath(BaseDocumentField, UnmappedFieldName, args.DocumentSerializer, args.SerializerRegistry),
                UnmappedFieldSerializer,
                UnmappedFieldSerializer,
                UnmappedFieldSerializer);

        public Type? TryGetBaseDocumentType(IBsonSerializer<TDocument> documentSerializer, IBsonSerializerRegistry serializerRegistry) =>
            UnmappedFieldDefinitionHelper.TryGetBaseDocumentType(BaseDocumentField, documentSerializer, serializerRegistry);
    }

    internal static class UnmappedFieldDefinitionHelper
    {
        public static string BuildFieldPath<TDocument>(
            FieldDefinition<TDocument>? baseDocumentField,
            string fieldName,
            IBsonSerializer<TDocument> documentSerializer,
            IBsonSerializerRegistry serializerRegistry)
        {
            var sb = new StringBuilder();
            if (baseDocumentField is not null)
            {
                var baseDocRenderedField = baseDocumentField.Render(new(documentSerializer, serializerRegistry));
                sb.Append(baseDocRenderedField.FieldName);
            }
            if (sb.Length > 0)
                sb.Append('.');
            sb.Append(fieldName);

            return sb.ToString();
        }

        public static Type? TryGetBaseDocumentType<TDocument>(
            FieldDefinition<TDocument>? baseDocumentField,
            IBsonSerializer<TDocument> documentSerializer,
            IBsonSerializerRegistry serializerRegistry)
        {
            if (baseDocumentField is null)
                return null;

            var renderedBaseDocumentField = baseDocumentField.Render(new(documentSerializer, serializerRegistry));
            var baseDocumentFieldSerializer = renderedBaseDocumentField.FieldSerializer;

            // Until serializer is an array serializer, go down to its item serializer.
            var baseDocumentSerializer = baseDocumentFieldSerializer;
            while (baseDocumentSerializer is IBsonArraySerializer arraySerializer)
            {
                if (!arraySerializer.TryGetItemSerializationInfo(out var itemSerializationInfo))
                    return null;
                baseDocumentSerializer = itemSerializationInfo.Serializer;
            }

            return baseDocumentSerializer.ValueType;
        }
    }
}
