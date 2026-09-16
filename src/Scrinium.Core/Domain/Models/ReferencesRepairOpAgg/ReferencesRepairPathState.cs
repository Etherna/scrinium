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

namespace Etherna.Scrinium.Core.Domain.Models.ReferencesRepairOpAgg
{
    /// <summary>
    /// What a references repair operation does to one reference element path of its
    /// collection, and how far it got. The operation is created with one of these per chosen
    /// path, so its plan is readable before anything runs.
    /// It is written once, like every other element of an operation log: the operation owns
    /// the transitions of its paths and replaces the state it advances, nothing mutates one
    /// of these from outside the aggregate.
    /// </summary>
    public class ReferencesRepairPathState : ModelBase
    {
        // Enums.
        public enum ExecutionState
        {
            Pending,
            Executing,
            Succeded,
            Skipped,
            Failed
        }

        // Constructors.
        /// <summary>
        /// The planned state of a path, before the operation runs.
        /// </summary>
        internal ReferencesRepairPathState(
            string elementPath,
            OriginDeleteMode repairMode)
            : this(elementPath, repairMode, ExecutionState.Pending, 0, 0, 0, null)
        { }

        internal ReferencesRepairPathState(
            string elementPath,
            OriginDeleteMode repairMode,
            ExecutionState state,
            long missingOriginIdsCount,
            long updatedDocumentsCount,
            long deletedDocumentsCount,
            string? errorMessage)
        {
            DeletedDocumentsCount = deletedDocumentsCount;
            ElementPath = elementPath;
            ErrorMessage = errorMessage;
            MissingOriginIdsCount = missingOriginIdsCount;
            RepairMode = repairMode;
            State = state;
            UpdatedDocumentsCount = updatedDocumentsCount;
        }
        protected ReferencesRepairPathState() { }

        // Properties.
        /// <summary>
        /// The documents deleted at this path, when the repair deletes the referencing ones.
        /// </summary>
        public virtual long DeletedDocumentsCount { get; protected set; }

        /// <summary>
        /// The reference element path, as the referencing documents nest it.
        /// </summary>
        public virtual string ElementPath { get; protected set; } = null!;

        /// <summary>
        /// The error that failed this path, when it failed.
        /// </summary>
        public virtual string? ErrorMessage { get; protected set; }

        /// <summary>
        /// The distinct referenced ids whose origin document doesn't exist, as found while
        /// repairing. A path kept by its mode is not scanned, and reports zero.
        /// </summary>
        public virtual long MissingOriginIdsCount { get; protected set; }

        /// <summary>
        /// What the repair does with the references of this path.
        /// </summary>
        public virtual OriginDeleteMode RepairMode { get; protected set; }

        public virtual ExecutionState State { get; protected set; }

        /// <summary>
        /// The documents updated by the reference removals at this path.
        /// </summary>
        public virtual long UpdatedDocumentsCount { get; protected set; }
    }
}
