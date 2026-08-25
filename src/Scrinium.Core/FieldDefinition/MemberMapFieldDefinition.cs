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
using Etherna.MongODM.Core.Serialization.Mapping;
using System;

namespace Etherna.MongODM.Core.FieldDefinition
{
    public class MemberMapFieldDefinition<TDocument> : FieldDefinition<TDocument>
    {
        // Fields.
        private readonly Func<ArrayElementRepresentation, string> undefinedArrayIndexSymbolSelector;
        private readonly Func<DocumentElementRepresentation, string> undefinedDocumentElementSymbolSelector;

        // Constructor.
        public MemberMapFieldDefinition(
            IMemberMap memberMap,
            string arrayUndefinedItemSymbol = "",
            string documentUndefinedElementSymbol = "",
            bool referToFinalItem = false) :
            this(memberMap,
                _ => arrayUndefinedItemSymbol,
                _ => documentUndefinedElementSymbol,
                referToFinalItem)
        { }

        public MemberMapFieldDefinition(
            IMemberMap memberMap,
            Func<ArrayElementRepresentation, string> undefinedArrayIndexSymbolSelector,
            Func<DocumentElementRepresentation, string> undefinedDocumentElementSymbolSelector,
            bool referToFinalItem = false)
        {
            MemberMap = memberMap;
            ReferToFinalItem = referToFinalItem;
            this.undefinedArrayIndexSymbolSelector = undefinedArrayIndexSymbolSelector;
            this.undefinedDocumentElementSymbolSelector = undefinedDocumentElementSymbolSelector;
        }

        // Properties.
        public IMemberMap MemberMap { get; }
        public bool ReferToFinalItem { get; }

        // Methods.
        public override RenderedFieldDefinition Render(RenderArgs<TDocument> args) =>
            new(MemberMap.RenderElementPath(
                    ReferToFinalItem,
                    undefinedArrayIndexSymbolSelector,
                    undefinedDocumentElementSymbolSelector),
                MemberMap.Serializer);
    }

    public class MemberMapFieldDefinition<TDocument, TField> : FieldDefinition<TDocument, TField>
    {
        // Fields.
        private readonly IBsonSerializer<TField>? customFieldSerializer;
        private readonly Func<ArrayElementRepresentation, string> undefinedArrayIndexSymbolSelector;
        private readonly Func<DocumentElementRepresentation, string> undefinedDocumentElementSymbolSelector;

        // Constructor.
        public MemberMapFieldDefinition(
            IMemberMap memberMap,
            string arrayUndefinedItemSymbol = "",
            string documentUndefinedElementSymbol = "",
            bool referToFinalItem = false,
            IBsonSerializer<TField>? customFieldSerializer = null) :
            this(memberMap,
                _ => arrayUndefinedItemSymbol,
                _ => documentUndefinedElementSymbol,
                referToFinalItem,
                customFieldSerializer)
        { }

        public MemberMapFieldDefinition(
            IMemberMap memberMap,
            Func<ArrayElementRepresentation, string> undefinedArrayIndexSymbolSelector,
            Func<DocumentElementRepresentation, string> undefinedDocumentElementSymbolSelector,
            bool referToFinalItem = false,
            IBsonSerializer<TField>? customFieldSerializer = null)
        {
            this.customFieldSerializer = customFieldSerializer;
            MemberMap = memberMap;
            this.undefinedArrayIndexSymbolSelector = undefinedArrayIndexSymbolSelector;
            this.undefinedDocumentElementSymbolSelector = undefinedDocumentElementSymbolSelector;
            ReferToFinalItem = referToFinalItem;
        }

        // Properties.
        public IMemberMap MemberMap { get; }
        public bool ReferToFinalItem { get; }

        // Methods.
        public override RenderedFieldDefinition<TField> Render(RenderArgs<TDocument> args)
        {
            IBsonSerializer<TField> valueSerializer =
                customFieldSerializer ??
                (IBsonSerializer<TField>)FieldValueSerializerHelper.GetSerializerForValueType(
                    MemberMap.Serializer,
                    MemberMap.DbContextEngine.SerializerRegistry,
                    typeof(TField),
                    args.PathRenderArgs.AllowScalarValueForArray);

            return new RenderedFieldDefinition<TField>(
                MemberMap.RenderElementPath(
                    ReferToFinalItem,
                    undefinedArrayIndexSymbolSelector,
                    undefinedDocumentElementSymbolSelector),
                valueSerializer,
                valueSerializer,
                MemberMap.Serializer);
        }
    }
}
