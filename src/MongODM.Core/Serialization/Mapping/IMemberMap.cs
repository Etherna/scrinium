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
using System;
using System.Collections.Generic;

namespace Etherna.MongODM.Core.Serialization.Mapping
{
    public interface IMemberMap
    {
        // Properties.
        IEnumerable<IMemberMap> AllDescendingMemberMaps { get; }

        BsonMemberMap BsonMemberMap { get; }

        IEnumerable<IMemberMap> ChildMemberMaps { get; }

        IDbContextEngine DbContext { get; }

        bool ElementPathHasUndefinedArrayIndex { get; }

        bool ElementPathHasUndefinedDocumentElement { get; }

        /// <summary>
        /// An unique identifier
        /// </summary>
        string Id { get; }

        IEnumerable<ElementRepresentationBase> InternalElementPath { get; }

        /// <summary>
        /// True if member is contained into a referenced entity model
        /// </summary>
        bool IsEntityReferenceMember { get; }

        bool IsGeneratedByActiveSchemas { get; }

        /// <summary>
        /// True if member is an entity Id
        /// </summary>
        bool IsIdMember { get; }

        IEnumerable<IMemberMap> MemberMapPath { get; }

        IModelMapSchema ModelMapSchema { get; }

        IMemberMap? OwnerEntityIdMap { get; }

        IMemberMap? ParentMemberMap { get; }

        IBsonSerializer Serializer { get; }

        // Methods.
        string RenderElementPath(
            bool referToFinalItem,
            Func<ArrayElementRepresentation, string> undefinedArrayIndexSymbolSelector,
            Func<DocumentElementRepresentation, string> undefinedDocumentElementSymbolSelector);

        string RenderInternalItemElementPath(
            Func<ArrayElementRepresentation, string> undefinedArrayIndexSymbolSelector,
            Func<DocumentElementRepresentation, string> undefinedDocumentElementSymbolSelector);
    }
}