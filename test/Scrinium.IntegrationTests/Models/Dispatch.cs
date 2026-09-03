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
    /// A value object embedded by <see cref="Message"/>, wrapping an <see cref="Envelope"/>
    /// into one more document level: the account references it hosts are the fourth
    /// member map level from the message root.
    /// </summary>
    public class Dispatch
    {
        // Constructors.
        public Dispatch(Envelope envelope)
        {
            Envelope = envelope;
        }
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        protected Dispatch() { }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        // Properties.
        public virtual Envelope Envelope { get; protected set; }
    }
}
