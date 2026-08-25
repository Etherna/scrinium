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

using Etherna.MongoDB.Driver;
using System.Diagnostics.CodeAnalysis;

namespace Etherna.Scrinium.Core.Utility
{
    /* A filtered view handed out by a guarded collection (e.g. by OfType): every
     * operation keeps the guards of the originating collection, with the same engine
     * and read-only flag, and reading the filter verifies the read permission. */
    [SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix")]
    internal sealed class LimitedAccessFilteredMongoCollection<TDocument>(
        IDbContextEngine dbContextEngine,
        IFilteredMongoCollection<TDocument> filteredMongoCollection,
        bool isReadOnly)
        : LimitedAccessMongoCollection<TDocument>(dbContextEngine, filteredMongoCollection, isReadOnly),
            IFilteredMongoCollection<TDocument>
    {
        // Properties.
        public FilterDefinition<TDocument> Filter
        {
            get
            {
                using var _ = EnterReadOperation();
                return filteredMongoCollection.Filter;
            }
        }
    }
}
