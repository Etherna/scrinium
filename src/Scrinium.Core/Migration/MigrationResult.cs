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

using Etherna.Scrinium.Core.Domain.Models.DbMigrationOpAgg;
using System;
using System.Collections.Generic;

namespace Etherna.Scrinium.Core.Migration
{
    public class MigrationResult
    {
        // Constructors.
        private MigrationResult() { }

        // Properties.
        /// <summary>
        /// The detail of the failing documents, capped at
        /// <see cref="DocumentMigration.MaxTrackedDocumentErrors"/> entries.
        /// <see cref="TotDocumentErrors"/> reports the full count.
        /// </summary>
        public IReadOnlyCollection<DocumentMigrationError> DocumentErrors { get; private set; } = [];
        public Exception? Exception { get; private set; }
        /// <summary>
        /// Number of documents processed without errors. Failing documents count
        /// on <see cref="TotDocumentErrors"/> instead.
        /// </summary>
        public long MigratedDocuments { get; private set; }
        /// <summary>
        /// Number of documents scanned by the migration: the migrated ones,
        /// plus the failing ones.
        /// </summary>
        public long ProcessedDocuments => MigratedDocuments + TotDocumentErrors;
        public bool Succeded { get; private set; }
        public long TotDocumentErrors { get; private set; }

        // Methods.
        public static MigrationResult Failed(
            long migratedDocuments,
            Exception? e = null,
            IReadOnlyCollection<DocumentMigrationError>? documentErrors = null,
            long totDocumentErrors = 0) =>
            new()
            {
                DocumentErrors = documentErrors ?? [],
                Exception = e,
                MigratedDocuments = migratedDocuments,
                Succeded = false,
                TotDocumentErrors = totDocumentErrors
            };

        public static MigrationResult Succeeded(long migratedDocuments) =>
            new()
            {
                MigratedDocuments = migratedDocuments,
                Succeded = true
            };
    }
}
