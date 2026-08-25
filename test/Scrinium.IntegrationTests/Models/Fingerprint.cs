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

namespace Etherna.MongODM.IntegrationTests.Models
{
    /// <summary>
    /// A value type identifying contents by their fingerprint, serialized with a custom
    /// serializer map: used both as entity id and as plain member type.
    /// </summary>
    public readonly struct Fingerprint(string value) : IEquatable<Fingerprint>
    {
        // Properties.
        public string Value { get; } = value;

        // Methods.
        public bool Equals(Fingerprint other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is Fingerprint other && Equals(other);
        public override int GetHashCode() => Value?.GetHashCode(StringComparison.Ordinal) ?? 0;
        public override string ToString() => Value ?? "";

        // Operators.
        public static bool operator ==(Fingerprint left, Fingerprint right) => left.Equals(right);
        public static bool operator !=(Fingerprint left, Fingerprint right) => !left.Equals(right);
    }
}
