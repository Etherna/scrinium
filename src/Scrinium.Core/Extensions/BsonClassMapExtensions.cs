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
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Serialization.Serializers;
using System;
using System.Linq.Expressions;
using System.Reflection;

namespace Etherna.Scrinium.Core.Extensions
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
            {
                /* The runtime type equality guaranteed by this branch can't be expressed to
                 * the type system: with the sealed serializer the direct interface cast is
                 * rejected at compile time, so hop through object to defer it to runtime,
                 * where it always succeeds. */
                return classMap.SetMemberSerializer(memberLambda, (IBsonSerializer<TMember>)(object)serializer);
            }
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
