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

using Etherna.MongoDB.Bson.Serialization;
using Etherna.MongODM.Core.Serialization.Mapping;
using Etherna.MongODM.Core.Serialization.Serializers;
using System;
using System.Collections.Generic;

namespace Etherna.MongODM.Core.Extensions
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

                if (serializer is IBsonDictionarySerializer dictionarySerializer)
                {
                    try
                    {
                        serializer = dictionarySerializer.ValueSerializer;
                        continue;
                    }
                    catch { }
                }

                if (serializer is IBsonArraySerializer arraySerializer &&
                    arraySerializer.TryGetItemSerializationInfo(out var itemSerializationInfo))
                {
                    serializer = itemSerializationInfo.Serializer;
                    continue;
                }

                break;
            }

            return null;
        }
    }
}
