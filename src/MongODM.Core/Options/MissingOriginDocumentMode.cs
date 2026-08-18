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

namespace Etherna.MongODM.Core.Options
{
    /// <summary>
    /// How a summary model reacts to a full load finding no origin document, because the
    /// referred document doesn't exist anymore on the origin collection: an inconsistency of
    /// the database, that only an exception makes observable. Declared per reference on
    /// <see cref="Serialization.Serializers.ReferenceSerializerConfiguration.MissingOriginDocument"/>,
    /// and carried by the summary models the reference deserializes.
    /// Values are ordered by strictness: a summary of the same document reached by more
    /// references materializes one single instance, keeping the strictest of their modes.
    /// </summary>
    public enum MissingOriginDocumentMode
    {
        /// <summary>Ignore the missing document.</summary>
        Silent = 0,

        /// <summary>
        /// Ignore the missing document, logging a warning once per model type and source
        /// repository, per db context scope.
        /// </summary>
        Warn = 1,

        /// <summary>
        /// Deny the load, throwing <see cref="Exceptions.MongodmMissingOriginDocumentException"/>. The default.
        /// </summary>
        Throw = 2
    }
}
