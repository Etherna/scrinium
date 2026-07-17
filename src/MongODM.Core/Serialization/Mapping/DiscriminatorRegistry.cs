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
using Etherna.MongoDB.Bson.Serialization.Conventions;
using Etherna.MongODM.Core.Conventions;
using Etherna.MongODM.Core.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace Etherna.MongODM.Core.Serialization.Mapping
{
    public class DiscriminatorRegistry : IDiscriminatorRegistry, IDisposable
    {
        // Fields.
        private readonly ReaderWriterLockSlim configLock = new(LockRecursionPolicy.SupportsRecursion);
        private readonly Dictionary<Type, IDiscriminatorConvention> discriminatorConventions = new();
        private readonly Dictionary<BsonValue, HashSet<Type>> discriminators = new();
        private readonly HashSet<Type> discriminatedTypes = [];
        
        private bool disposed;
        private ILogger logger = null!;
        private IDbContextEngine dbContextEngine = null!;

        // Initializer.
        public void Initialize(IDbContextEngine dbContextEngine, ILogger logger)
        {
            if (IsInitialized)
                throw new InvalidOperationException("Instance already initialized");
            this.dbContextEngine = dbContextEngine ?? throw new ArgumentNullException(nameof(dbContextEngine));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

            IsInitialized = true;

            this.logger.DiscriminatorRegistryInitialized(dbContextEngine.Options.DbName);
        }
        
        // Dispose.
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed) return;

            // Dispose managed resources.
            if (disposing)
            {
                configLock.Dispose();
            }

            disposed = true;
        }

        // Properties.
        public bool IsInitialized { get; private set; }

        // Methods.
        public void AddDiscriminator(Type type, BsonValue discriminator)
        {
            // Checks.
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(discriminator);

            var typeInfo = type.GetTypeInfo();
            if (typeInfo.IsInterface)
                throw new BsonSerializationException($"Discriminators can only be registered for classes, not for interface {type.FullName}.");

            // Add discriminator.
            configLock.EnterWriteLock();
            try
            {
                if (!discriminators.TryGetValue(discriminator, out HashSet<Type>? hashSet))
                {
                    hashSet = new HashSet<Type>();
                    discriminators.Add(discriminator, hashSet);
                }

                if (hashSet.Add(type))
                {
                    //mark all base types as discriminated (so we know that it's worth reading a discriminator)
                    for (var baseType = typeInfo.BaseType; baseType != null; baseType = baseType.GetTypeInfo().BaseType)
                        discriminatedTypes.Add(baseType);
                }
            }
            finally
            {
                configLock.ExitWriteLock();
            }
        }

        public void AddDiscriminatorConvention(Type type, IDiscriminatorConvention convention)
        {
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(convention);

            configLock.EnterWriteLock();
            try
            {
                if (!discriminatorConventions.TryAdd(type, convention))
                    throw new BsonSerializationException($"There is already a discriminator convention registered for type {type.FullName}.");
            }
            finally
            {
                configLock.ExitWriteLock();
            }
        }

        public bool IsTypeDiscriminated(Type type)
        {
            var typeInfo = type.GetTypeInfo();
            return typeInfo.IsInterface || discriminatedTypes.Contains(type);
        }

        public Type LookupActualType(Type nominalType, BsonValue? discriminator)
        {
            if (discriminator == null)
                return nominalType;

            configLock.EnterReadLock();
            try
            {
                Type? actualType = null;

                var nominalTypeInfo = nominalType.GetTypeInfo();
                if (discriminators.TryGetValue(discriminator, out HashSet<Type>? hashSet))
                {
                    foreach (var type in hashSet)
                    {
                        if (nominalTypeInfo.IsAssignableFrom(type))
                        {
                            if (actualType == null)
                                actualType = type;
                            else
                                throw new BsonSerializationException($"Ambiguous discriminator '{discriminator}'.");
                        }
                    }
                }

                if (actualType == null)
                    throw new BsonSerializationException($"Unknown discriminator value '{discriminator}'.");

                return actualType;
            }
            finally
            {
                configLock.ExitReadLock();
            }
        }

        public IDiscriminatorConvention LookupDiscriminatorConvention(Type type)
        {
            configLock.EnterReadLock();
            try
            {
                if (discriminatorConventions.TryGetValue(type, out IDiscriminatorConvention? convention))
                    return convention;
            }
            finally
            {
                configLock.ExitReadLock();
            }

            configLock.EnterWriteLock();
            try
            {
                if (!discriminatorConventions.TryGetValue(type, out IDiscriminatorConvention? convention))
                {
                    var typeInfo = type.GetTypeInfo();
                    if (type == typeof(object))
                    {
                        //if there is no convention registered for object register the default one
                        convention = new HierarchicalProxyTolerantDiscriminatorConvention(dbContextEngine, "_t");
                        AddDiscriminatorConvention(typeof(object), convention);
                    }
                    else if (typeInfo.IsInterface)
                    {
                        // TODO: should convention for interfaces be inherited from parent interfaces?
                        convention = LookupDiscriminatorConvention(typeof(object));
                        AddDiscriminatorConvention(type, convention);
                    }
                    else //type is not typeof(object), or interface
                    {
                        //inherit the discriminator convention from the closest parent that has one
                        convention = LookupDiscriminatorConvention(typeInfo.BaseType!);

                        //register the convention for current type
                        AddDiscriminatorConvention(type, convention);
                    }
                }

                return convention;
            }
            finally
            {
                configLock.ExitWriteLock();
            }
        }
    }
}
