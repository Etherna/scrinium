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

using Etherna.MongoDB.Bson;
using Etherna.MongoDB.Bson.Serialization;
using Etherna.Scrinium.Core.Serialization.Serializers;
using System;
using System.Reflection;

namespace Etherna.Scrinium.Core.Serialization.Providers
{
    /// <summary>
    /// Serialization provider backed by the map registry: serves every mappable type with a
    /// <see cref="MappedSerializerAdapter{TModel}"/>, delegating to the serializer mapped by
    /// the map registry, whatever kind of map serves the type.
    /// </summary>
    public class MapRegistrySerializationProvider(IDbContextEngine dbContextEngine)
        : BsonSerializationProviderBase
    {
        // Methods.
        public override IBsonSerializer? GetSerializer(Type type, IBsonSerializerRegistry serializerRegistry)
        {
            ArgumentNullException.ThrowIfNull(type);

            var typeInfo = type.GetTypeInfo();
            if (typeInfo is { IsGenericType: true, ContainsGenericParameters: true })
                throw new ArgumentException(
                    $"Generic type {BsonUtils.GetFriendlyTypeName(type)} has unassigned type parameters.",
                    nameof(type));

            if ((typeInfo.IsClass || typeInfo is { IsValueType: true, IsPrimitive: false }) &&
                !typeof(Array).GetTypeInfo().IsAssignableFrom(type) &&
                !typeof(Enum).GetTypeInfo().IsAssignableFrom(type))
            {
                /* The adapter delegates to the serializer mapped by the map registry: a
                 * lookup can run while maps are still registering, so the mapped serializer
                 * can't be resolved here. */
                var serializerAdapterDefinition = typeof(MappedSerializerAdapter<>);
                var serializerAdapterType = serializerAdapterDefinition.MakeGenericType(type);
                return (IBsonSerializer)Activator.CreateInstance(serializerAdapterType, dbContextEngine)!;
            }

            return null;
        }
    }
}
