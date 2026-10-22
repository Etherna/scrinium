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

using System;

namespace Etherna.Scrinium.Core.Domain.Models.DbMigrationOpAgg
{
    public abstract class MigrationLogBase : ModelBase
    {
        // Enums.
        public enum ExecutionState
        {
            Executing,
            Succeeded,
            Skipped,
            Failed,
            /// <summary>
            /// The operation closed while the step was executing: what the log reports is
            /// what the step did until the stop, not its end.
            /// </summary>
            Interrupted
        }

        // Constructors.
        protected MigrationLogBase(ExecutionState state)
        {
            CreationDateTime = DateTimeOffset.UtcNow;
            State = state;
        }
        protected MigrationLogBase() { }

        // Properties.
        public virtual ExecutionState State { get; protected set; }
        public virtual DateTimeOffset CreationDateTime { get; protected set; }

        // Methods.
        /// <summary>
        /// Close an executing log as interrupted: the operation closed while its step was
        /// executing, so the log keeps what the step reported until the stop, as its end.
        /// </summary>
        internal void Interrupt()
        {
            if (State != ExecutionState.Executing)
                throw new InvalidOperationException($"Can't interrupt a migration log whose state is {State}");

            State = ExecutionState.Interrupted;
        }
    }
}
