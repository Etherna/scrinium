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
using System;
using System.Collections.Generic;

namespace Etherna.MongODM.Core.Repositories
{
    public class RepositoryOptions<TModel>(string name)
    {
        // Properties.
        /// <summary>
        /// Custom indexes to build on the collection. An index without an explicit name
        /// takes the name rendered from its keys. An index starting with an ascending or
        /// descending key on a referenced document id path replaces the automatic index
        /// on that path.
        /// </summary>
        public IEnumerable<(IndexKeysDefinition<TModel> keys, CreateIndexOptions<TModel> options)> IndexBuilders { get; set; } = [];

        /// <summary>
        /// When true, the repository denies any write on its collection, index management
        /// included. Reads work normally. Useful to consume a collection owned by another
        /// application, avoiding any possibility to write on it.
        /// </summary>
        public bool IsReadOnly { get; set; }

        public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

        /// <summary>
        /// When true, saving the tracked changes of a model replaces the whole document,
        /// instead of updating only the changed members.
        /// </summary>
        public bool SaveWithDocumentReplace { get; set; }
    }
}
