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

namespace Etherna.Scrinium.Core.Domain.Models.DbMigrationOpAgg
{
    public abstract class MigrationLogBase : ModelBase
    {
        // Enums.
        public enum ExecutionState
        {
            Executing,
            Succeded,
            Skipped,
            Failed
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
    }
}
