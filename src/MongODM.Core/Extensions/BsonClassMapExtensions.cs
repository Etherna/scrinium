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
using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.Serialization.Serializers;
using System;
using System.Linq.Expressions;
using System.Reflection;

namespace Etherna.MongODM.Core.Extensions
{
    public static class BsonClassMapExtensions
    {
        public static bool IsEntity(this BsonClassMap classMap)
        {
            ArgumentNullException.ThrowIfNull(classMap);

            return classMap.IdMemberMap != null;
        }

        public static void SetBaseClassMap(this BsonClassMap classMap, BsonClassMap baseClassMap)
        {
            typeof(BsonClassMap).GetField("_baseClassMap", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(classMap, baseClassMap);
        }

        public static BsonMemberMap SetMemberSerializer<TModel, TMember>(
            this BsonClassMap<TModel> classMap,
            Expression<Func<TModel, TMember>> memberLambda,
            IBsonSerializer<TMember> serializer)
        {
            ArgumentNullException.ThrowIfNull(classMap);

            var member = classMap.GetMemberMap(memberLambda);
            member ??= classMap.MapMember(memberLambda);
            return member.SetSerializer(serializer);
        }

        public static BsonMemberMap SetMemberSerializer<TModel, TMember, TSerializer, TKey>(
            this BsonClassMap<TModel> classMap,
            Expression<Func<TModel, TMember>> memberLambda,
            ReferenceSerializer<TSerializer, TKey> serializer)
        where TMember : class, TSerializer
        where TSerializer : class, IEntityModel<TKey>
        {
            ArgumentNullException.ThrowIfNull(serializer);

            if (typeof(TMember) == typeof(TSerializer))
                return classMap.SetMemberSerializer(memberLambda, (IBsonSerializer<TMember>)serializer);
            else
                return classMap.SetMemberSerializer(memberLambda, new EntityModelSerializerAdapter<TMember, TSerializer, TKey>(serializer));
        }

        public static IBsonSerializer ToSerializer(
            this BsonClassMap classMap)
        {
            ArgumentNullException.ThrowIfNull(classMap);
            
            var classMapSerializerDefinition = typeof(BsonClassMapSerializer<>);
            var classMapSerializerType = classMapSerializerDefinition.MakeGenericType(classMap.ClassType);
            return (IBsonSerializer)Activator.CreateInstance(classMapSerializerType, classMap)!;
        }
    }
}
