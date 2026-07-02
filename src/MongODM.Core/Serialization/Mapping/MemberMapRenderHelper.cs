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

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace Etherna.MongODM.Core.Serialization.Mapping
{
    public static class MemberMapRenderHelper
    {
        [SuppressMessage("Performance", "CA1851:Possible multiple enumerations of \'IEnumerable\' collection")]
        public static string RenderElementPath(
            IEnumerable<IMemberMap> memberMapsPath,
            bool referToFinalItem,
            Func<ArrayElementRepresentation, string> undefinedArrayIndexSymbolSelector,
            Func<DocumentElementRepresentation, string> undefinedDocumentElementSymbolSelector)
        {
            ArgumentNullException.ThrowIfNull(memberMapsPath);
            
            var sb = new StringBuilder();

            foreach (var memberMap in memberMapsPath)
            {
                if (sb.Length != 0)
                    sb.Append('.');

                sb.Append(memberMap.BsonMemberMap.ElementName);

                //don't render final item element path, if not required
                if (referToFinalItem || memberMap != memberMapsPath.Last())
                    sb.Append(memberMap.RenderInternalItemElementPath(
                        undefinedArrayIndexSymbolSelector,
                        undefinedDocumentElementSymbolSelector));
            }

            return sb.ToString();
        }

        public static string RenderInternalItemElementPath(
            IEnumerable<ElementRepresentationBase> elementsPath,
            Func<ArrayElementRepresentation, string> undefinedArrayIndexSymbolSelector,
            Func<DocumentElementRepresentation, string> undefinedDocumentElementSymbolSelector)
        {
            ArgumentNullException.ThrowIfNull(elementsPath);
            ArgumentNullException.ThrowIfNull(undefinedArrayIndexSymbolSelector);
            ArgumentNullException.ThrowIfNull(undefinedDocumentElementSymbolSelector);
            
            var sb = new StringBuilder();

            foreach (var element in elementsPath)
            {
                switch (element)
                {
                    case ArrayElementRepresentation arrayElementPathRepresentation:
                        sb.Append(arrayElementPathRepresentation.ItemIndex is null ?
                            undefinedArrayIndexSymbolSelector(arrayElementPathRepresentation) :
                            $".{arrayElementPathRepresentation.ItemIndex}");
                        break;
                    case DocumentElementRepresentation documentElementPathRepresentation:
                        sb.Append(documentElementPathRepresentation.ElementName is null ?
                            undefinedDocumentElementSymbolSelector(documentElementPathRepresentation) :
                            $".{documentElementPathRepresentation.ElementName}");
                        break;
                    default: throw new NotSupportedException();
                }
            }

            return sb.ToString();
        }
    }
}
