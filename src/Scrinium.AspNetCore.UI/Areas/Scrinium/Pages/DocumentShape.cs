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

using System.Collections.Generic;

namespace Etherna.Scrinium.AspNetCore.UI.Areas.Scrinium.Pages
{
    /// <summary>
    /// The shape of the document, or of the sub-document, written by a model map schema.
    /// </summary>
    public sealed class DocumentShape(
        IEnumerable<DocumentElement> elements,
        bool isActiveSchema,
        bool isCycle,
        string modelTypeName,
        string schemaId)
    {
        // Properties.
        /// <summary>
        /// The elements the schema writes, in the order it writes them, beside the schema id
        /// element carrying <see cref="SchemaId"/>. Empty on a cycle, where the elements are
        /// the ones of the same shape above.
        /// </summary>
        public IEnumerable<DocumentElement> Elements { get; } = elements;

        /// <summary>
        /// True when the schema is the active one of its model map, the only one writing new
        /// documents: any other shapes documents written by a previous version of the
        /// application, until a migration rewrites them.
        /// </summary>
        public bool IsActiveSchema { get; } = isActiveSchema;

        /// <summary>
        /// True when the shape closes a cycle of the model graph, repeating one already
        /// expanded above it: a document nests here the same shape again, as deep as its data
        /// goes.
        /// </summary>
        public bool IsCycle { get; } = isCycle;

        /// <summary>
        /// The name of the model type the schema maps.
        /// </summary>
        public string ModelTypeName { get; } = modelTypeName;

        /// <summary>
        /// The model map schema id stamped on the documents of this shape.
        /// </summary>
        public string SchemaId { get; } = schemaId;
    }
}
