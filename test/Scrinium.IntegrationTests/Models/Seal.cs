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

namespace Etherna.MongODM.IntegrationTests.Models
{
    /// <summary>
    /// An entity referring an artifact by fingerprint: the custom serialized value type
    /// used as plain member, beside its entity id role on <see cref="Artifact"/>.
    /// </summary>
    public class Seal : EntityModelBase<string>
    {
        // Constructors.
        public Seal(Fingerprint artifactFingerprint)
        {
            ArtifactFingerprint = artifactFingerprint;
        }
        protected Seal() { }

        // Properties.
        public virtual Fingerprint ArtifactFingerprint { get; set; }
    }
}
