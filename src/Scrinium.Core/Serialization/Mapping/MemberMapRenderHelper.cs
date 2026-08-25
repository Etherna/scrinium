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

using Etherna.Scrinium.Core.Exceptions;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace Etherna.Scrinium.Core.Serialization.Mapping
{
    public static class MemberMapRenderHelper
    {
        // Consts.
        /// <summary>
        /// Name of the array filter addressing the array items hosting a referenced id.
        /// </summary>
        internal const string ArrayFilterName = "idfilter";

        // Methods.
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

        // Internals.
        /// <summary>
        /// The undefined array index symbol selector of an update field path: every array level
        /// above the filtered one addresses all its positions, the filtered one addresses the
        /// items selected by the array filter.
        /// </summary>
        /// <param name="lastUndefinedArrayElement">The array level the array filter addresses</param>
        /// <returns>The symbol selector</returns>
        internal static Func<ArrayElementRepresentation, string> BuildArrayFilterFieldSelector(
            ArrayElementRepresentation? lastUndefinedArrayElement) =>
            arrayElement => arrayElement != lastUndefinedArrayElement ?
                ".$[]" :                  //select all array items
                $".$[{ArrayFilterName}]"; //else, filter in array items

        /// <summary>
        /// The last array element with an undefined index on the element path of a member map:
        /// the array level an update filters on the items hosting the referenced id.
        /// </summary>
        /// <param name="memberMap">The member map addressed by the update</param>
        /// <returns>The array element, or null when its path crosses no such array</returns>
        internal static ArrayElementRepresentation? FindLastUndefinedArrayElement(IMemberMap memberMap)
        {
            ArgumentNullException.ThrowIfNull(memberMap);

            return memberMap.MemberMapPath
                .SelectMany(mm => mm.InternalElementPath
                    .OfType<ArrayElementRepresentation>()
                    .Where(arrayElement => arrayElement.ItemIndex is null))
                .LastOrDefault();
        }

        /// <summary>
        /// The path addressing the referenced id inside the array items selected by the array
        /// filter, prefixed by the filter name.
        /// </summary>
        /// <param name="idMemberMap">The id member map of the reference</param>
        /// <param name="lastUndefinedArrayElement">The array level the array filter addresses</param>
        /// <returns>The array filter id path</returns>
        internal static string RenderArrayFilterIdPath(
            IMemberMap idMemberMap,
            ArrayElementRepresentation lastUndefinedArrayElement)
        {
            ArgumentNullException.ThrowIfNull(idMemberMap);
            ArgumentNullException.ThrowIfNull(lastUndefinedArrayElement);

            return $"{ArrayFilterName}{string.Join(".",
                idMemberMap.MemberMapPath
                    .SkipWhile(mm => mm != lastUndefinedArrayElement.MemberMap) //take all final member maps in path from the last with undefined array index
                    .Select(mm =>
                    {
                        //if is the member map hosting the filtered array, render internal path only after it
                        var internalElementPathToRender = mm.InternalElementPath;
                        if (mm == lastUndefinedArrayElement.MemberMap)
                            internalElementPathToRender = internalElementPathToRender.Reverse()
                                                                                     .TakeWhile(element => element != lastUndefinedArrayElement)
                                                                                     .Reverse();

                        var renderedInternalElementPath = RenderInternalItemElementPath(
                            internalElementPathToRender,
                            _ => throw new ScriniumElementPathRenderingException("Can't exist arrays with undefined index here"),
                            _ => throw new ScriniumElementPathRenderingException("Can't render field with an unknown document key in path"));

                        return mm != lastUndefinedArrayElement.MemberMap ?
                            mm.BsonMemberMap.ElementName + renderedInternalElementPath :
                            renderedInternalElementPath;
                    }))}";
        }
    }
}
