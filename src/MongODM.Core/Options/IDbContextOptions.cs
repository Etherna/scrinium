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

namespace Etherna.MongODM.Core.Options
{
    public interface IDbContextOptions
    {
        public string ConnectionString { get; }
        public string DbName { get; }
        public string DbOperationsCollectionName { get; }

        /// <summary>
        /// True to save the changed models of <see cref="IDbContext.SaveChangesAsync"/> into an
        /// implicit transaction, when the connected deployment supports transactions (replica
        /// set, or sharded cluster). The support is detected at runtime from the cluster
        /// topology: with unsupporting deployments, like standalone servers, saves stay plain.
        /// Set false to disable implicit transactions in any case.
        /// </summary>
        public bool EnableTransactionsWithReplicaSet { get; }

        public string? Identifier { get; }

        /// <summary>
        /// How the db context reacts to implicit lazy loads of summary model members:
        /// load logging a warning (the default), load silently, or deny them throwing.
        /// Preload members explicitly with <see cref="IDbContext.LoadValuesAsync{TModel}(TModel, System.Linq.Expressions.Expression{System.Func{TModel, object?}}[])"/>.
        /// </summary>
        public ImplicitLazyLoadMode ImplicitLazyLoad { get; }

        /// <summary>
        /// True to deny any write on the database from this db context: document writes,
        /// index management, seeding and migrations. Reads work normally. Useful to consume
        /// a database owned by another application, avoiding any possibility to write on it.
        /// </summary>
        public bool IsReadOnly { get; }

        /// <summary>
        /// Configuration of the document element carrying the model map schema id.
        /// </summary>
        public ModelMapSchemaIdOptions ModelMapSchemaId { get; }
    }
}