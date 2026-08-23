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

using Etherna.MongoDB.Driver;
using Etherna.MongODM.Core.Migration;
using System;
using System.Collections.Generic;

namespace Etherna.MongODM.Core.Options
{
    public class DbContextOptions : IDbContextOptions
    {
        // Fields.
        private readonly List<Type> _childDbContextTypes = new();

        // Properties.
        public IEnumerable<Type> ChildDbContextTypes => _childDbContextTypes;
        public string ConnectionString { get; set; } = "mongodb://localhost/localDb";
        public string DbLockCollectionName { get; set; } = "_db_lock";
        /* The error never reports the connection string: its user info section can carry
         * credentials, and this value flows into log lines as a structured field. */
        public string DbName => MongoUrl.Create(ConnectionString).DatabaseName ??
            throw new InvalidOperationException(
                "Connection string doesn't specify a database name. Declare it as its path segment, as in \"mongodb://localhost:27017/mydbname\"");
        public string DbOperationsCollectionName { get; set; } = "_db_ops";
        public bool EnableTransactionsWithReplicaSet { get; set; } = true;
        public TimeSpan ExclusiveAccessDrainTimeout { get; set; } = TimeSpan.FromMinutes(5);
        public string? Identifier { get; set; }
        public ReactionMode ImplicitLazyLoad { get; set; } = ReactionMode.Warn;
        public bool IsReadOnly { get; set; }
        public int MigrationCallbackEveryTotDocuments { get; set; } = 500;
        public int MigrationEvictEveryTotDocuments { get; set; } = DocumentMigration.DefaultEvictEveryTotDocuments;
        public ReactionMode NotPropagatedReferences { get; set; } = ReactionMode.Warn;

        // Methods.
        public void ParentFor<TDbContext>() where
            TDbContext : class, IDbContext
        {
            if (!_childDbContextTypes.Contains(typeof(TDbContext)))
                _childDbContextTypes.Add(typeof(TDbContext));
        }
    }
}
