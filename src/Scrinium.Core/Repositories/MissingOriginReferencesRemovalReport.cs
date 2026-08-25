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

namespace Etherna.MongODM.Core.Repositories
{
    /// <summary>
    /// The result of a removal of the references to missing origin documents on a collection:
    /// one removal per scanned reference element path, and the paths the scan can't verify,
    /// whose references stay untouched.
    /// </summary>
    public class MissingOriginReferencesRemovalReport(
        IReadOnlyCollection<MissingOriginReferencesPathRemoval> pathRemovals,
        IReadOnlyCollection<string> unverifiableElementPaths)
    {
        // Properties.
        /// <summary>
        /// One removal per scanned reference element path, the clean ones included.
        /// </summary>
        public IReadOnlyCollection<MissingOriginReferencesPathRemoval> PathRemovals { get; } = pathRemovals;

        /// <summary>
        /// The reference element paths the scan can't verify: paths it can't address server
        /// side (an unknown document key, or a fixed array position, being in the path), and
        /// paths whose origin repository doesn't resolve on the current scope.
        /// </summary>
        public IReadOnlyCollection<string> UnverifiableElementPaths { get; } = unverifiableElementPaths;
    }
}
