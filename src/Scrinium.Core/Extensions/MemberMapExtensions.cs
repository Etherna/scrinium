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

using Etherna.MongoDB.Bson.Serialization;
using Etherna.Scrinium.Core.Serialization.Mapping;
using Etherna.Scrinium.Core.Serialization.Serializers;
using System;
using System.Collections.Generic;

namespace Etherna.Scrinium.Core.Extensions
{
    internal static class MemberMapExtensions
    {
        /// <summary>
        /// The reference serializer hosting the sub-document of a reference id member: the
        /// parent member serializer, unwrapped from its container serializers like the
        /// internal element path walk does. Self reporting serializers close the walk,
        /// instead of looping.
        /// </summary>
        /// <param name="idMemberMap">The id member map of the reference</param>
        /// <returns>The hosting reference serializer, if any</returns>
        public static IReferenceSerializer? TryFindHostingReferenceSerializer(this IMemberMap idMemberMap)
        {
            ArgumentNullException.ThrowIfNull(idMemberMap);

            var serializer = idMemberMap.ParentMemberMap?.Serializer;
            HashSet<IBsonSerializer> exploredSerializers = [];
            while (serializer is not null && exploredSerializers.Add(serializer))
            {
                if (serializer is IReferenceSerializer referenceSerializer)
                    return referenceSerializer;

                if (!serializer.TryGetContainerChildSerializer(out var childSerializer))
                    break;
                serializer = childSerializer;
            }

            return null;
        }
    }
}
