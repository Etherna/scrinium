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

using System;

namespace Etherna.Scrinium.IntegrationTests.Models
{
    public class Web3Account : AccountBase
    {
        // Constructors.
        /// <summary>
        /// Convert a web2 account into a web3 one, keeping its id.
        /// </summary>
        public Web3Account(Web2Account source, string etherAddress)
            : base((source ?? throw new ArgumentNullException(nameof(source))).Username)
        {
            Id = source.Id;
            EtherAddress = etherAddress;
        }
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        protected Web3Account() { }
#pragma warning restore CS8618

        // Properties.
        public virtual string EtherAddress { get; protected set; }
    }
}
