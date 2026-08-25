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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;

namespace Etherna.Scrinium.Core
{
    public static class ReflectionHelper
    {
        private static readonly Dictionary<Type, IEnumerable<PropertyInfo>> propertyRegistry = new();
        private static readonly ReaderWriterLockSlim propertyRegistryLock = new();

        public static PropertyInfo FindPropertyImplementation(PropertyInfo interfacePropertyInfo, Type actualType)
        {
            ArgumentNullException.ThrowIfNull(interfacePropertyInfo);
            ArgumentNullException.ThrowIfNull(actualType);

            var interfaceType = interfacePropertyInfo.DeclaringType!;

            // An interface map must be used because there is no
            // other officially documented way to derive the explicitly
            // implemented property name.
            var interfaceMap = actualType.GetInterfaceMap(interfaceType);

            var interfacePropertyAccessors = interfacePropertyInfo.GetAccessors(true);

            var actualPropertyAccessors = interfacePropertyAccessors.Select(interfacePropertyAccessor =>
            {
                var index = Array.IndexOf(interfaceMap.InterfaceMethods, interfacePropertyAccessor);

                return interfaceMap.TargetMethods[index];
            });

            // Binding must be done by accessor methods because interface
            // maps only map accessor methods and do not map properties.
            return actualType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(propertyInfo =>
                {
                    // we are looking for a property that implements all the required accessors
                    var propertyAccessors = propertyInfo.GetAccessors(true);
                    return actualPropertyAccessors.All(x => propertyAccessors.Contains(x));
                });
        }

        public static MemberInfo GetMemberInfoFromLambda<TModel, TMember>(
            Expression<Func<TModel, TMember>> memberLambda,
            Type? actualType = null)
        {
            ArgumentNullException.ThrowIfNull(memberLambda);

            var body = memberLambda.Body;
            MemberExpression memberExpression;
            switch (body.NodeType)
            {
                case ExpressionType.MemberAccess:
                    memberExpression = (MemberExpression)body;
                    break;
                case ExpressionType.Convert:
                    var convertExpression = (UnaryExpression)body;
                    memberExpression = (MemberExpression)convertExpression.Operand;
                    break;
                default:
                    throw new InvalidOperationException("Invalid lambda expression");
            }
            var memberInfo = memberExpression.Member;
            switch (memberInfo.MemberType)
            {
                case MemberTypes.Field:
                    break;
                case MemberTypes.Property:
                    if (actualType?.IsInterface == false &&
                        memberInfo.DeclaringType!.IsInterface)
                    {
                        memberInfo = FindPropertyImplementation((PropertyInfo)memberInfo, actualType);
                    }
                    break;
                default:
                    memberInfo = null;
                    break;
            }
            if (memberInfo == null)
            {
                throw new InvalidOperationException("Invalid lambda expression");
            }
            return memberInfo;
        }

        public static object? GetValue(object source, MemberInfo memberInfo)
        {
            if (memberInfo is FieldInfo fieldInfo)
                return fieldInfo.GetValue(source);

            if (memberInfo is PropertyInfo propertyInfo && propertyInfo.CanRead)
                return propertyInfo.GetValue(source);

            return null;
        }

        /// <summary>
        /// Return the list of writable instance property of a type
        /// </summary>
        /// <returns>The list of properties</returns>
        public static IEnumerable<PropertyInfo> GetWritableInstanceProperties(Type objectType)
        {
            ArgumentNullException.ThrowIfNull(objectType);

            propertyRegistryLock.EnterReadLock();
            try
            {
                if (propertyRegistry.TryGetValue(objectType, out IEnumerable<PropertyInfo>? value))
                    return value;
            }
            finally
            {
                propertyRegistryLock.ExitReadLock();
            }

            propertyRegistryLock.EnterWriteLock();
            try
            {
                if (!propertyRegistry.TryGetValue(objectType, out IEnumerable<PropertyInfo>? value))
                {
                    var typeStack = new List<Type>();
                    var stackType = objectType;
                    do
                    {
                        typeStack.Add(stackType);
                        stackType = stackType.BaseType;
                    } while (stackType != null);
                    
                    //materialize, so the registry entry is a snapshot instead of a query re-run at every enumeration
                    value = typeStack
                        .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        .Where(prop => prop.CanWrite)
                        .ToArray();
                    propertyRegistry.Add(objectType, value);
                }
                return value;
            }
            finally
            {
                propertyRegistryLock.ExitWriteLock();
            }
        }

        public static void SetValue(object destination, MemberInfo memberInfo, object? value)
        {
            if (memberInfo is FieldInfo fieldInfo)
            {
                if (!fieldInfo.IsInitOnly)
                    fieldInfo.SetValue(destination, value);
                return;
            }

            if (memberInfo is PropertyInfo propertyInfo && propertyInfo.CanWrite)
                propertyInfo.SetValue(destination, value);
        }
    }
}
