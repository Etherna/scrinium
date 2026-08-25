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
using System.Threading;

namespace Etherna.Scrinium.IntegrationTests.Models
{
    /// <summary>
    /// A mapped entity never legitimately hosted by the object shaped members of
    /// <see cref="Capsule"/>: a document selecting it with a discriminator must not have
    /// it instantiated. Constructions count into a global counter, so tests can assert
    /// that no instantiation happened.
    /// </summary>
    public class Secret : EntityModelBase<string>
    {
        // Fields.
        private static int _totalConstructedInstances;

        // Constructors.
        public Secret(string value)
        {
            Interlocked.Increment(ref _totalConstructedInstances);
            Value = value;
        }
        protected Secret()
        {
            Interlocked.Increment(ref _totalConstructedInstances);
        }

        // Properties.
        public static int TotalConstructedInstances => _totalConstructedInstances;
        public virtual string? Value { get; set; }
    }
}
