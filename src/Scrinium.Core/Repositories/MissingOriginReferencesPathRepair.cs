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

using Etherna.Scrinium.Core.Options;

namespace Etherna.Scrinium.Core.Repositories
{
    /// <summary>
    /// The repair applied to the references to missing origin documents of one reference
    /// element path of a collection.
    /// </summary>
    public class MissingOriginReferencesPathRepair(
        string elementPath,
        OriginDeleteMode repairMode,
        long missingOriginIdsCount,
        long updatedDocumentsCount,
        long deletedDocumentsCount)
    {
        // Properties.
        /// <summary>
        /// The documents deleted by the repair, when it deletes the referencing documents.
        /// </summary>
        public long DeletedDocumentsCount { get; } = deletedDocumentsCount;

        /// <summary>
        /// The reference element path, as the referencing documents nest it.
        /// </summary>
        public string ElementPath { get; } = elementPath;

        /// <summary>
        /// The distinct referenced ids whose origin document doesn't exist on any origin
        /// repository of the path. A kept path is not scanned: it reports zero.
        /// </summary>
        public long MissingOriginIdsCount { get; } = missingOriginIdsCount;

        /// <summary>
        /// What the repair did with the references of the path:
        /// <see cref="OriginDeleteMode.KeepReference"/> left them as they are,
        /// <see cref="OriginDeleteMode.RemoveReference"/> removed them,
        /// <see cref="OriginDeleteMode.DeleteReferencingDocument"/> deleted the documents
        /// carrying them.
        /// </summary>
        public OriginDeleteMode RepairMode { get; } = repairMode;

        /// <summary>
        /// The documents updated by the reference removals. A document is counted once per
        /// missing origin id removed from it.
        /// </summary>
        public long UpdatedDocumentsCount { get; } = updatedDocumentsCount;
    }
}
