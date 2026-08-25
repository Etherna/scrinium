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

using System.Collections.Generic;

namespace Etherna.MongODM.IntegrationTests.Models
{
    /// <summary>
    /// A value object embedded by <see cref="Message"/>, hosting account references
    /// inside a nested document.
    /// </summary>
    public class Envelope
    {
        // Fields.
        private List<AccountBase> _recipients = [];

        // Constructors.
        public Envelope(IEnumerable<AccountBase> recipients)
        {
            Recipients = recipients;
        }
        protected Envelope() { }

        // Properties.
        public virtual IEnumerable<AccountBase> Recipients
        {
            get => _recipients;
            protected set => _recipients = [.. value ?? []];
        }
    }
}
