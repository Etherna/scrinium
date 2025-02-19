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

using Etherna.MongoDB.Bson;
using Etherna.MongoDB.Bson.Serialization;
using Etherna.MongODM.Core.Serialization.Serializers;
using System;
using System.Globalization;
using System.Reflection;

namespace Etherna.MongODM.Core.Serialization.Providers
{
    public class ModelMapSerializationProvider(DbContext dbContext)
        : BsonSerializationProviderBase
    {
        // Methods.
        public override IBsonSerializer? GetSerializer(Type type, IBsonSerializerRegistry serializerRegistry)
        {
            ArgumentNullException.ThrowIfNull(type, nameof(type));

            var typeInfo = type.GetTypeInfo();
            if (typeInfo.IsGenericType && typeInfo.ContainsGenericParameters)
            {
                var message = string.Format(CultureInfo.InvariantCulture, "Generic type {0} has unassigned type parameters.", BsonUtils.GetFriendlyTypeName(type));
                throw new ArgumentException(message, nameof(type));
            }

            if ((typeInfo.IsClass || (typeInfo.IsValueType && !typeInfo.IsPrimitive)) &&
                !typeof(Array).GetTypeInfo().IsAssignableFrom(type) &&
                !typeof(Enum).GetTypeInfo().IsAssignableFrom(type))
            {
                var modelMapSerializerDefinition = typeof(ModelMapSerializer<>);
                var modelMapSerializerType = modelMapSerializerDefinition.MakeGenericType(type);
                return (IBsonSerializer)Activator.CreateInstance(modelMapSerializerType, dbContext)!;
            }

            return null;
        }
    }
}
