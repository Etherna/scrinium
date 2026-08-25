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

namespace Etherna.Scrinium.IntegrationTests.Models
{
    /// <summary>
    /// An entity living only while its subject track does: its reference declares the
    /// referencing document delete on origin delete.
    /// </summary>
    public class Royalty : EntityModelBase<string>
    {
        // Constructors.
        public Royalty(string beneficiary, Track subject)
        {
            Beneficiary = beneficiary;
            Subject = subject;
        }
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        protected Royalty() { }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        // Properties.
        public virtual string Beneficiary { get; set; }
        public virtual Track? Subject { get; protected set; }
    }
}
