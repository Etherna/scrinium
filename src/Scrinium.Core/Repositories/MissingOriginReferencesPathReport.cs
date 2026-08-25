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

namespace Etherna.Scrinium.Core.Repositories
{
    /// <summary>
    /// The missing origin documents found scanning one reference element path of a collection.
    /// </summary>
    public class MissingOriginReferencesPathReport(
        string elementPath,
        IReadOnlyCollection<string> originRepositoryNames,
        long missingOriginIdsCount,
        IReadOnlyCollection<string> trackedMissingOriginIds,
        long referencingDocumentsCount)
    {
        // Consts.
        /// <summary>
        /// Most missing origin ids a path report lists. The scan keeps counting beyond it:
        /// <see cref="MissingOriginIdsCount"/> reports the full count.
        /// </summary>
        public const int MaxTrackedMissingOriginIds = 100;

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
        /// The origin repositories the referenced ids were verified against.
        /// </summary>
        public IReadOnlyCollection<string> OriginRepositoryNames { get; } = originRepositoryNames;

        /// <summary>
        /// The documents carrying at least one reference to a tracked missing origin id.
        /// When the tracking cap drops some ids, this is a lower bound.
        /// </summary>
        public long ReferencingDocumentsCount { get; } = referencingDocumentsCount;

        /// <summary>
        /// The missing origin ids, as stored on the referencing documents, capped at
        /// <see cref="MaxTrackedMissingOriginIds"/> entries.
        /// </summary>
        public IReadOnlyCollection<string> TrackedMissingOriginIds { get; } = trackedMissingOriginIds;
    }
}
