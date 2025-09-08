// Copyright 2021-present Etherna SA
// This file is part of Etherna Gateway.
// 
// Etherna Gateway is free software: you can redistribute it and/or modify it under the terms of the
// GNU Affero General Public License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// 
// Etherna Gateway is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY;
// without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU Affero General Public License for more details.
// 
// You should have received a copy of the GNU Affero General Public License along with Etherna Gateway.
// If not, see <https://www.gnu.org/licenses/>.

namespace Etherna.MongODM.Core.Domain.Models.DbMigrationOpAgg
{
    public class BuildNewIndexesMigrationLog : MigrationLogBase
    {
        // Constructors.
        public BuildNewIndexesMigrationLog(
            string repository,
            ExecutionState state)
            : base(state)
        {
            Repository = repository;
        }
        protected BuildNewIndexesMigrationLog() { }

        // Properties.
        public virtual string Repository { get; protected set; } = null!;
    }
}