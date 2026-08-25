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

namespace Etherna.Scrinium.Core.Repositories
{
    /// <summary>
    /// The references to missing origin documents removed from one reference element path
    /// of a collection.
    /// </summary>
    public class MissingOriginReferencesPathRemoval(
        string elementPath,
        long missingOriginIdsCount,
        long updatedDocumentsCount)
    {
        // Properties.
        /// <summary>
        /// The reference element path, as the referencing documents nest it.
        /// </summary>
        public string ElementPath { get; } = elementPath;

        /// <summary>
        /// The distinct referenced ids whose origin document doesn't exist on any origin
        /// repository of the path.
        /// </summary>
        public long MissingOriginIdsCount { get; } = missingOriginIdsCount;

        /// <summary>
        /// The documents updated by the removals. A document is counted once per missing
        /// origin id removed from it.
        /// </summary>
        public long UpdatedDocumentsCount { get; } = updatedDocumentsCount;
    }
}
