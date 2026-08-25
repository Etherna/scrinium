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

using Etherna.Scrinium.Core.Domain.Models;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Etherna.Scrinium.Core.ProxyModels
{
    /// <summary>
    /// Tells whether handing out a value of a given type (from a model getter) lets external code
    /// mutate the owning model's persisted state without going through the model itself. That is the
    /// case for a mutable collection, or a complex type exposing a public setter or a public business
    /// method (its body can't be inspected, so it's assumed to mutate), or, recursively, a type whose
    /// members expose such mutation. It's a conservative check: it never reports a truly immutable type
    /// as mutable-safe, only the opposite, so a change is never missed. Entity references stop the
    /// recursion: their changes are tracked on their own repository, not on the owner document. Value
    /// types are handed out by copy, so only their reference members can leak.
    /// </summary>
    public static class MutabilityAnalyzer
    {
        // Fields.
        private static readonly ConcurrentDictionary<Type, bool> cache = new();
        private static readonly HashSet<string> nonMutatingMethodNames =
            ["Deconstruct", "Equals", "GetHashCode", "ToString"];
        private static readonly HashSet<Type> immutableValueTypes =
            [typeof(DateTime), typeof(DateTimeOffset), typeof(Guid), typeof(TimeSpan), typeof(decimal)];

        // Methods.
        /// <summary>
        /// True if reading a value of the given type can leak an autonomous mutation of the owner model.
        /// </summary>
        /// <param name="type">The exposed value type</param>
        public static bool ExposesAutonomousMutation(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            return ExposesAutonomousMutation(type, []);
        }

        // Helpers.
        private static bool ExposesAutonomousMutation(Type type, HashSet<Type> visiting)
        {
            if (cache.TryGetValue(type, out var cached))
                return cached;

            /* A type met again while still analyzing it is a reference cycle: assume it mutable, the
             * safe side. A wrongly mutable type only costs a redundant diff at save; a wrongly immutable
             * one would miss a change. */
            if (!visiting.Add(type))
                return true;

            var result = ComputeExposesAutonomousMutation(type, visiting);

            visiting.Remove(type);
            cache[type] = result;
            return result;
        }

        private static bool ComputeExposesAutonomousMutation(Type type, HashSet<Type> visiting)
        {
            // Nullable<T> exposes what T exposes.
            if (Nullable.GetUnderlyingType(type) is { } underlyingType)
                return ExposesAutonomousMutation(underlyingType, visiting);

            // Primitives, enums, strings and known immutable value types can't be mutated in place.
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || immutableValueTypes.Contains(type))
                return false;

            // Entity references are tracked on their own repository: they don't make the owner mutable.
            if (typeof(IEntityModel).IsAssignableFrom(type))
                return false;

            // A mutable collection (add/remove/replace) exposes mutation regardless of its elements.
            if (IsMutableCollection(type))
                return true;

            // A read-only collection can still expose mutation through its element type.
            if (TryGetEnumerableElementType(type, out var elementType))
                return ExposesAutonomousMutation(elementType, visiting);

            /* A value type is copied out by the getter, so its own setters and methods can't mutate the
             * owner; only its reference members can leak, checked by the property recursion below. A
             * reference type instead is shared: a public setter, or a public method that isn't a property
             * accessor, an operator, an object override or a compiler generated / record member, could
             * change it. */
            if (!type.IsValueType &&
                (HasPublicSetter(type) || HasPublicWritableField(type) || HasPublicMutatingMethod(type)))
                return true;

            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                       .Where(property => property.GetIndexParameters().Length == 0)
                       .Any(property => ExposesAutonomousMutation(property.PropertyType, visiting)) ||
                   type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                       .Any(field => ExposesAutonomousMutation(field.FieldType, visiting));
        }

        private static bool HasPublicWritableField(Type type) =>
            type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Any(field => field is { IsInitOnly: false, IsLiteral: false });

        private static bool HasPublicMutatingMethod(Type type) =>
            type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Any(method => !method.IsSpecialName &&                          //not a property/event/operator accessor
                               method.DeclaringType != typeof(object) &&         //not an object method (GetType, ...)
                               !method.Name.StartsWith('<') &&                   //not compiler generated (record clone, ...)
                               !nonMutatingMethodNames.Contains(method.Name));

        private static bool HasPublicSetter(Type type) =>
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Any(property => property.SetMethod is { IsPublic: true } setter && !IsInitOnly(setter));

        private static bool IsInitOnly(MethodInfo setter) =>
            setter.ReturnParameter.GetRequiredCustomModifiers()
                  .Any(modifier => modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit");

        private static bool IsMutableCollection(Type type)
        {
            if (type.IsArray)
                return true;
            if (typeof(IList).IsAssignableFrom(type) || typeof(IDictionary).IsAssignableFrom(type))
                return true;
            return type.GetInterfaces().Prepend(type)
                       .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollection<>));
        }

        private static bool TryGetEnumerableElementType(Type type, out Type elementType)
        {
            var enumerableInterface = type.GetInterfaces().Prepend(type)
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            if (enumerableInterface is not null)
            {
                elementType = enumerableInterface.GetGenericArguments()[0];
                return true;
            }

            elementType = null!;
            return false;
        }
    }
}
