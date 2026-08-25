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

namespace Etherna.Scrinium.Core.Domain.Models.DbMigrationOpAgg
{
    public class DocumentMigrationLog : MigrationLogBase
    {
        // Fields.
        private List<DocumentMigrationError> _errors = [];

        // Constructors.
        public DocumentMigrationLog(
            string collectionName,
            ExecutionState state,
            long totMigratedDocs,
            IEnumerable<DocumentMigrationError>? errors = null,
            long totErrorDocs = 0)
            : base(state)
        {
            CollectionName = collectionName;
            Errors = errors ?? [];
            TotErrorDocs = totErrorDocs;
            TotMigratedDocs = totMigratedDocs;
        }
        protected DocumentMigrationLog() { }

        // Properties.
        public virtual string CollectionName { get; protected set; } = null!;
        public virtual IEnumerable<DocumentMigrationError> Errors
        {
            get => _errors;
            protected set => _errors = [.. value ?? []];
        }
        public virtual long TotErrorDocs { get; protected set; }
        public virtual long TotMigratedDocs { get; protected set; }
    }
}
