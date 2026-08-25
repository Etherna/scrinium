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

namespace Etherna.Scrinium.IntegrationTests.Models
{
    /// <summary>
    /// A mutable object embedded by <see cref="Tally"/>: reading it flags its owner for a
    /// diff at save, and its members are updatable one by one, also server side.
    /// </summary>
    public class Score
    {
        // Constructors.
        public Score(int total, string label)
        {
            Total = total;
            Label = label;
        }
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        protected Score() { }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        // Properties.
        public virtual string Label { get; set; }
        public virtual int Total { get; set; }
    }
}
