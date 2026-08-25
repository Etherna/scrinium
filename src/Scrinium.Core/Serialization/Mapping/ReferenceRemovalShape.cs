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
using Etherna.Scrinium.Core.Exceptions;
using System.Linq;

namespace Etherna.Scrinium.Core.Serialization.Mapping
{
    /// <summary>
    /// The update shape removing a reference from the documents hosting it, precomputed from
    /// the id member map of the reference: a reference hosted as an array item is pulled out
    /// of its array, any other one is set to null, addressed through an array filter on the
    /// referenced id when arrays are in its path. Serves the missing origin references
    /// removal and the deletion propagation task.
    /// </summary>
    internal sealed class ReferenceRemovalShape
    {
        // Fields.
        private readonly string? arrayFilterIdPath;
        private readonly string? pullFieldPath;
        private readonly string? pullIdElementPath;
        private readonly string? setFieldPath;

        // Constructor.
        private ReferenceRemovalShape(
            string? arrayFilterIdPath,
            string idElementPath,
            string? pullFieldPath,
            string? pullIdElementPath,
            string? setFieldPath)
        {
            this.arrayFilterIdPath = arrayFilterIdPath;
            IdElementPath = idElementPath;
            this.pullFieldPath = pullFieldPath;
            this.pullIdElementPath = pullIdElementPath;
            this.setFieldPath = setFieldPath;
        }

        // Properties.
        /// <summary>
        /// The dotted query path of the referenced ids, arrays traversed implicitly.
        /// </summary>
        public string IdElementPath { get; }

        // Methods.
        /// <summary>
        /// The filter matching the documents still hosting a reference to the id at the path.
        /// </summary>
        /// <param name="referencedIdValue">The referenced id, as stored</param>
        /// <returns>The filter document</returns>
        public BsonDocument BuildFilter(BsonValue referencedIdValue) =>
            new(IdElementPath, new BsonDocument("$eq", referencedIdValue));

        /// <summary>
        /// The update removing the references to the id, with the array filter document
        /// addressing the hosting array items when the path requires one.
        /// </summary>
        /// <param name="referencedIdValue">The referenced id, as stored</param>
        /// <returns>The update document, with its optional array filter</returns>
        public (BsonDocument Update, BsonDocument? ArrayFilter) BuildUpdate(BsonValue referencedIdValue)
        {
            if (pullFieldPath is not null)
                return (
                    new BsonDocument("$pull", new BsonDocument(
                        pullFieldPath,
                        new BsonDocument(pullIdElementPath!, new BsonDocument("$eq", referencedIdValue)))),
                    null);

            return (
                new BsonDocument("$set", new BsonDocument(setFieldPath!, BsonNull.Value)),
                arrayFilterIdPath is null ? null :
                    new BsonDocument(arrayFilterIdPath, new BsonDocument("$eq", referencedIdValue)));
        }

        /// <summary>
        /// Build the removal shape of a reference from its id member map. Paths that can't be
        /// addressed with a filter — an unknown document key (a dictionary in document
        /// representation), or a fixed array position (the value slot of an ArrayOfArrays
        /// dictionary) — build no shape.
        /// </summary>
        /// <param name="idMemberMap">The id member map of the reference</param>
        /// <returns>The removal shape, or null when the path can't be addressed</returns>
        public static ReferenceRemovalShape? TryCreate(IMemberMap idMemberMap)
        {
            if (idMemberMap is null ||
                idMemberMap.ElementPathHasUndefinedDocumentElement ||
                idMemberMap.MemberMapPath
                    .SelectMany(memberMap => memberMap.InternalElementPath)
                    .OfType<ArrayElementRepresentation>()
                    .Any(arrayElement => arrayElement.ItemIndex is not null))
                return null;

            var idElementPath = idMemberMap.RenderElementPath(
                referToFinalItem: true,
                _ => "",
                _ => throw new ScriniumElementPathRenderingException("Can't render field with an unknown document key in path"));

            var referenceMemberMap = idMemberMap.ParentMemberMap!;

            string? arrayFilterIdPath = null;
            string? pullFieldPath = null;
            string? pullIdElementPath = null;
            string? setFieldPath = null;
            if (referenceMemberMap.InternalElementPath.LastOrDefault() is ArrayElementRepresentation { ItemIndex: null })
            {
                // The reference is an array item: removing it pulls it out of its array.
                /* The rendered path ends with the positional symbol of the array hosting the
                 * items, that the $pull addresses as a whole: strip it, keeping the "all
                 * positions" symbol on any outer array level. */
                var renderedPath = referenceMemberMap.RenderElementPath(
                    referToFinalItem: true,
                    _ => ".$[]",
                    _ => throw new ScriniumElementPathRenderingException("Can't render field with an unknown document key in path"));
                pullFieldPath = renderedPath[..^".$[]".Length];
                pullIdElementPath = idMemberMap.BsonMemberMap.ElementName;
            }
            else
            {
                // The reference is a single valued element: removing it sets it to null.
                /* Every array level above the reference addresses all its positions, except
                 * the last one, filtered on the items nesting the referenced id. */
                var lastUndefinedArrayElement = MemberMapRenderHelper.FindLastUndefinedArrayElement(referenceMemberMap);

                setFieldPath = referenceMemberMap.RenderElementPath(
                    referToFinalItem: true,
                    MemberMapRenderHelper.BuildArrayFilterFieldSelector(lastUndefinedArrayElement),
                    _ => throw new ScriniumElementPathRenderingException("Can't render field with an unknown document key in path"));

                if (lastUndefinedArrayElement is not null)
                    arrayFilterIdPath = MemberMapRenderHelper.RenderArrayFilterIdPath(idMemberMap, lastUndefinedArrayElement);
            }

            return new ReferenceRemovalShape(
                arrayFilterIdPath,
                idElementPath,
                pullFieldPath,
                pullIdElementPath,
                setFieldPath);
        }
    }
}
