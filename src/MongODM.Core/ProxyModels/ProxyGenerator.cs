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

using Castle.DynamicProxy;
using Etherna.MongODM.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Etherna.MongODM.Core.ProxyModels
{
    public class ProxyGenerator(
        ILoggerFactory loggerFactory,
        Castle.DynamicProxy.IProxyGenerator proxyGeneratorCore)
        : IProxyGenerator, IDisposable
    {
        // Fields.
        private bool disposed;

        private readonly Dictionary<Type,
            (Type[] AdditionalInterfaces, Func<IDbContextEngine, IInterceptor[]> InterceptorInstancerSelector)> modelConfigurationDictionary = new();
        private readonly ReaderWriterLockSlim modelConfigurationDictionaryLock = new(LockRecursionPolicy.SupportsRecursion);

        private readonly Dictionary<Type, Type> proxyTypeDictionary = new();
        private readonly ReaderWriterLockSlim proxyTypeDictionaryLock = new(LockRecursionPolicy.SupportsRecursion);

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
                modelConfigurationDictionaryLock.Dispose();
                proxyTypeDictionaryLock.Dispose();
            }

            disposed = true;
        }

        // Properties.
        public bool DisableCreationWithProxyTypes { get; set; }

        // Methods.
        public object CreateInstance(
            Type type,
            IDbContextEngine dbContext,
            params object[] constructorArguments)
        {
            ArgumentNullException.ThrowIfNull(dbContext);
            ArgumentNullException.ThrowIfNull(type);

            // If creation of proxy models are disabled, create a simple model instance.
            if (DisableCreationWithProxyTypes)
            {
                return Activator.CreateInstance(
                    type,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    constructorArguments,
                    null)!;
            }

            // Get configuration.
            (Type[] AdditionalInterfaces, Func<IDbContextEngine, IInterceptor[]> InterceptorInstancerSelector) configuration = (null!, null!);
            modelConfigurationDictionaryLock.EnterReadLock();
            bool configurationFound = false;
            try
            {
                if (modelConfigurationDictionary.TryGetValue(type, out var conf))
                {
                    configuration = conf;
                    configurationFound = true;
                }
            }
            finally
            {
                modelConfigurationDictionaryLock.ExitReadLock();
            }

            if (!configurationFound)
            {
                modelConfigurationDictionaryLock.EnterWriteLock();
                try
                {
                    if (modelConfigurationDictionary.TryGetValue(type, out var conf))
                    {
                        configuration = conf;
                    }
                    else
                    {
                        var additionalInterfaces = GetAdditionalInterfaces(type);
                        configuration = (additionalInterfaces, GetInterceptorInstancer(type, additionalInterfaces));
                        modelConfigurationDictionary.Add(type, configuration);
                    }
                }
                finally
                {
                    modelConfigurationDictionaryLock.ExitWriteLock();
                }
            }

            // Generate model.
            var proxyModel = proxyGeneratorCore.CreateClassProxy(
                type,
                configuration.AdditionalInterfaces,
                ProxyGenerationOptions.Default,
                constructorArguments,
                configuration.InterceptorInstancerSelector(dbContext));

            // Add to proxy type dictionary.
            var addToproxyTypeDictionary = false;
            proxyTypeDictionaryLock.EnterReadLock();
            try
            {
                addToproxyTypeDictionary = !proxyTypeDictionary.ContainsKey(type);
            }
            finally
            {
                proxyTypeDictionaryLock.ExitReadLock();
            }

            if (addToproxyTypeDictionary)
            {
                proxyTypeDictionaryLock.EnterWriteLock();
                try
                {
                    if (!proxyTypeDictionary.ContainsKey(type))
                    {
                        proxyTypeDictionary.Add(type, proxyModel.GetType());
                    }
                }
                finally
                {
                    proxyTypeDictionaryLock.ExitWriteLock();
                }
            }

            return proxyModel;
        }

        public TModel CreateInstance<TModel>(IDbContextEngine dbContext, params object[] constructorArguments) =>
            (TModel)CreateInstance(typeof(TModel), dbContext, constructorArguments);

        public bool IsProxyType(Type type)
        {
            proxyTypeDictionaryLock.EnterReadLock();
            try
            {
                return proxyTypeDictionary.ContainsValue(type);
            }
            finally
            {
                proxyTypeDictionaryLock.ExitReadLock();
            }
        }

        public Type PurgeProxyType(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);

            return IsProxyType(type) ?
                type.BaseType! :
                type;
        }

        // Protected virtual methods.
        protected virtual IEnumerable<Type> GetCustomAdditionalInterfaces(Type modelType) => [];

        protected virtual IEnumerable<Func<IDbContextEngine, IInterceptor>> GetCustomInterceptorInstancer(
            Type modelType,
            IEnumerable<Type> additionalInterfaces) => [];

        // Helpers.
        private Type[] GetAdditionalInterfaces(Type modelType)
        {
            var interfaces = new List<Type>();

            // Add custom additional interfaces.
            interfaces.AddRange(GetCustomAdditionalInterfaces(modelType));

            // Add internal additional interfaces.
            if (modelType.GetInterfaces().Contains(typeof(IEntityModel))) //only if is IEntityModel.
            {
                interfaces.Add(typeof(IAuditable));
                interfaces.Add(typeof(IReferenceable));
            }

            return interfaces.ToArray();
        }

        private Func<IDbContextEngine, IInterceptor[]> GetInterceptorInstancer(
            Type modelType,
            IEnumerable<Type> additionalInterfaces)
        {
            var interceptorInstancers = new List<Func<IDbContextEngine, IInterceptor>>();

            // Add custom interceptor instancers.
            interceptorInstancers.AddRange(GetCustomInterceptorInstancer(modelType, additionalInterfaces));

            // Add internal interceptor instancers.
            if (modelType.GetInterfaces().Contains(typeof(IEntityModel))) //only if is IEntityModel.
            {
                var entityModelType = modelType.GetInterfaces().First(
                    i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntityModel<>));
                var entityModelKeyType = entityModelType.GetGenericArguments().Single();

                //auditableInterceptor
                var auditableInterceptorType = typeof(AuditableInterceptor<>).MakeGenericType(modelType);

                interceptorInstancers.Add(_ => (IInterceptor)Activator.CreateInstance(
                    auditableInterceptorType,
                    additionalInterfaces)!);

                //referenceableInterceptor
                var referenceableInterceptorType = typeof(ReferenceableInterceptor<,>).MakeGenericType(modelType, entityModelKeyType);

                var referenceableInterceptorLoggerType = typeof(Logger<>).MakeGenericType(referenceableInterceptorType);
                var referenceableInterceptorLogger = Activator.CreateInstance(referenceableInterceptorLoggerType, loggerFactory);

                interceptorInstancers.Add(dbContext => (IInterceptor)Activator.CreateInstance(
                    referenceableInterceptorType,
                    additionalInterfaces,
                    dbContext,
                    referenceableInterceptorLogger)!);
            }

            return dbContext => (from instancer in interceptorInstancers
                                 select instancer(dbContext)).ToArray();
        }
    }
}
