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

namespace Etherna.MongODM.Core
{
    public static class ReflectionHelper
    {
        private static readonly Dictionary<Type, IEnumerable<PropertyInfo>> propertyRegistry = new();
        private static readonly ReaderWriterLockSlim propertyRegistryLock = new();

        public static MemberInfo FindProperty(LambdaExpression lambdaExpression)
        {
            ArgumentNullException.ThrowIfNull(lambdaExpression, nameof(lambdaExpression));

            Expression expressionToCheck = lambdaExpression;

            bool done = false;

            while (!done)
            {
                switch (expressionToCheck.NodeType)
                {
                    case ExpressionType.Convert:
                        expressionToCheck = ((UnaryExpression)expressionToCheck).Operand;
                        break;
                    case ExpressionType.Lambda:
                        expressionToCheck = ((LambdaExpression)expressionToCheck).Body;
                        break;
                    case ExpressionType.MemberAccess:
                        var memberExpression = (MemberExpression)expressionToCheck;

                        if (memberExpression.Expression!.NodeType != ExpressionType.Parameter &&
                            memberExpression.Expression.NodeType != ExpressionType.Convert)
                        {
                            throw new ArgumentException(
                                $"Expression '{lambdaExpression}' must resolve to top-level member and not any child object's properties. Use a custom resolver on the child type or the AfterMap option instead.",
                                nameof(lambdaExpression));
                        }

                        MemberInfo member = memberExpression.Member;

                        return member;
                    default:
                        done = true;
                        break;
                }
            }

            throw new InvalidOperationException();
        }

        public static PropertyInfo FindPropertyImplementation(PropertyInfo interfacePropertyInfo, Type actualType)
        {
            ArgumentNullException.ThrowIfNull(interfacePropertyInfo, nameof(interfacePropertyInfo));
            ArgumentNullException.ThrowIfNull(actualType, nameof(actualType));

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
            ArgumentNullException.ThrowIfNull(memberLambda, nameof(memberLambda));

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

        public static TMember GetValueFromLambda<TModel, TMember>(TModel source, Expression<Func<TModel, TMember>> memberLambda)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));

            var memberInfo = GetMemberInfoFromLambda(memberLambda, source.GetType());
            return (TMember)GetValue(source, memberInfo)!;
        }

        /// <summary>
        /// Return the list of writable instance property of a type
        /// </summary>
        /// <returns>The list of properties</returns>
        public static IEnumerable<PropertyInfo> GetWritableInstanceProperties(Type objectType)
        {
            ArgumentNullException.ThrowIfNull(objectType, nameof(objectType));

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
                    
                    value = typeStack
                        .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        .Where(prop => prop.CanWrite);
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

        public static void SetValue<TModel, TMember>(TModel destination, Expression<Func<TModel, TMember>> memberLambda, TMember value)
        {
            if (destination is null)
                throw new ArgumentNullException(nameof(destination));

            var memberInfo = GetMemberInfoFromLambda(memberLambda, destination.GetType());
            SetValue(destination, memberInfo, value);
        }
    }
}
