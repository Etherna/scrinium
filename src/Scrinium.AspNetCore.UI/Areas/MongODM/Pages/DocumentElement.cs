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

using System.Collections.Generic;

namespace Etherna.Scrinium.AspNetCore.UI.Areas.MongODM.Pages
{
    /// <summary>
    /// An element written into a document by a model map schema.
    /// </summary>
    public sealed class DocumentElement(
        string containerSuffix,
        string elementName,
        bool hasReferencedModelRepository,
        bool isReference,
        bool isUpdatePropagated,
        IEnumerable<DocumentShape> shapes,
        string typeName)
    {
        // Properties.
        /// <summary>
        /// The containers the element wraps its values into, one symbol each: <c>[]</c> for an
        /// array, <c>{}</c> for a dictionary written with its keys as element names. Empty when
        /// the element carries the value directly.
        /// </summary>
        public string ContainerSuffix { get; } = containerSuffix;

        /// <summary>
        /// The name of the element on the document.
        /// </summary>
        public string ElementName { get; } = elementName;

        /// <summary>
        /// True when a repository of the db context handles every referenced model type of a
        /// reference element. Without one, its documents are saved on another db context, and
        /// their updates never propagate to this summary.
        /// </summary>
        public bool HasReferencedModelRepository { get; } = hasReferencedModelRepository;

        /// <summary>
        /// True when the element carries the summary of a referenced document, instead of an
        /// embedded value.
        /// </summary>
        public bool IsReference { get; } = isReference;

        /// <summary>
        /// True when an update of the referenced document rewrites the summaries carried here.
        /// An element addressed by an unknown document key can't be filtered, and the
        /// dependencies update task skips it.
        /// </summary>
        public bool IsUpdatePropagated { get; } = isUpdatePropagated;

        /// <summary>
        /// The shapes the element value can take, one per model map schema writing it: empty
        /// when the element carries a value instead of a sub-document.
        /// </summary>
        public IEnumerable<DocumentShape> Shapes { get; } = shapes;

        /// <summary>
        /// The name of the type of the carried value, containers excluded.
        /// </summary>
        public string TypeName { get; } = typeName;
    }
}
