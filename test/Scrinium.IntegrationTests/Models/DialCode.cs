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

using System;

namespace Etherna.Scrinium.IntegrationTests.Models
{
    /// <summary>
    /// A value type identifying lockers by the position of a named dial, serialized with a
    /// custom serializer map as a document whose first element name derives from the value:
    /// the id shape exercising the operator expression guard of the repository id filters.
    /// </summary>
    public readonly struct DialCode(string dial, int position) : IEquatable<DialCode>
    {
        // Properties.
        public string Dial { get; } = dial;
        public int Position { get; } = position;

        // Methods.
        public bool Equals(DialCode other) => Dial == other.Dial && Position == other.Position;
        public override bool Equals(object? obj) => obj is DialCode other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Dial, Position);
        public override string ToString() => $"{Dial}:{Position}";

        // Operators.
        public static bool operator ==(DialCode left, DialCode right) => left.Equals(right);
        public static bool operator !=(DialCode left, DialCode right) => !left.Equals(right);
    }
}
