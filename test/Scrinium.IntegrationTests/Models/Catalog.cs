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

using Etherna.Scrinium.Core.Domain.Models;
using System.Collections.Generic;

namespace Etherna.Scrinium.IntegrationTests.Models
{
    /// <summary>
    /// An entity referencing posts through dictionary members: the array of documents
    /// representation keeps the referenced ids addressable server side, while the document
    /// representation writes the dictionary keys as element names, unknown to the maps.
    /// </summary>
    public class Catalog : EntityModelBase<string>
    {
        // Properties.
        public virtual IDictionary<string, Post> IndexedPosts { get; set; } = new Dictionary<string, Post>();
        public virtual IDictionary<string, Post> LabeledPosts { get; set; } = new Dictionary<string, Post>();
    }
}
