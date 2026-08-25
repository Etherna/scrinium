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

using Etherna.Scrinium.Core.Repositories;
using Etherna.Scrinium.Core.Serialization.Serializers;
using System;

namespace Etherna.Scrinium.Core.Extensions
{
    internal static class ReferenceSerializerExtensions
    {
        /// <summary>
        /// The source repository of a reference, resolved on a db context like a lazy load
        /// would do: a source declared on a db context not reachable from the given one
        /// resolves nothing, instead of failing the caller flow.
        /// </summary>
        /// <param name="referenceSerializer">The reference serializer</param>
        /// <param name="dbContext">The db context resolving the source</param>
        /// <returns>The source repository, if resolvable</returns>
        public static IRepository? TryResolveSourceRepository(this IReferenceSerializer referenceSerializer, IDbContext dbContext)
        {
            ArgumentNullException.ThrowIfNull(referenceSerializer);

            try
            {
                return referenceSerializer.SourceRepositorySelector?.Invoke(dbContext);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }
}
