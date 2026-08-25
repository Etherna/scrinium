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

using Etherna.MongODM.Core.Domain.Models;
using System.Collections.Generic;

namespace Etherna.MongODM.IntegrationTests.Models
{
    /// <summary>
    /// An entity referencing tracks through references following the default origin delete
    /// policy — a single member, an array of references, dictionaries in array of documents
    /// and array of arrays representation, whose paths stay addressable, and a dictionary
    /// in document representation, whose path the propagation can't address — plus a
    /// reference explicitly declaring to keep the reference on origin delete.
    /// </summary>
    public class Mixtape : EntityModelBase<string>
    {
        // Constructors.
        public Mixtape(string name)
        {
            Name = name;
        }
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        protected Mixtape() { }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        // Properties.
        public virtual Track? Highlight { get; set; }
        public virtual IDictionary<string, Track> IndexedTracks { get; set; } = new Dictionary<string, Track>();
        public virtual IDictionary<string, Track> LabeledTracks { get; set; } = new Dictionary<string, Track>();
        public virtual string Name { get; set; }
        public virtual Track? Pinned { get; set; }
        public virtual IDictionary<string, Track> RankedTracks { get; set; } = new Dictionary<string, Track>();
        public virtual IEnumerable<Track> Tracks { get; set; } = [];
    }
}
