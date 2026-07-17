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
        public IEnumerable<(IndexKeysDefinition<TModel> keys, CreateIndexOptions<TModel> options)> IndexBuilders { get; set; } = [];
        public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

        /// <summary>
        /// When true, saving the tracked changes of a model replaces the whole document,
        /// instead of updating only the changed members.
        /// </summary>
        public bool SaveWithDocumentReplace { get; set; }
    }
}
