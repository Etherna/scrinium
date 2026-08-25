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

using Etherna.Scrinium.Core.Domain.Models;
using System.Collections.Generic;

namespace Etherna.Scrinium.IntegrationTests.Models
{
    /// <summary>
    /// An entity counting events per key into a dictionary, and embedding a <see cref="Score"/>:
    /// the nested documents whose elements an upsert can update one by one.
    /// </summary>
    public class Tally : EntityModelBase<string>
    {
        // Fields.
        private Dictionary<string, int> counters = [];

        // Constructors.
        public Tally(string subject, IReadOnlyDictionary<string, int> counters, Score score)
        {
            Subject = subject;
            Counters = counters;
            Score = score;
        }
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        protected Tally() { }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        // Properties.
        public virtual IReadOnlyDictionary<string, int> Counters
        {
            get => counters;
            protected set => counters = new Dictionary<string, int>(value ?? new Dictionary<string, int>());
        }
        public virtual Score Score { get; protected set; }
        public virtual string Subject { get; protected set; }
    }
}
