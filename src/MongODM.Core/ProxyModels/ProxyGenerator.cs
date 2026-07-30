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

using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.ExecContext;
using Etherna.MongODM.Core.Utility;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Etherna.MongODM.Core.ProxyModels
{
    public class ProxyGenerator(IExecutionContext executionContext)
        : IProxyGenerator
    {
        // Fields.
        private readonly ConcurrentDictionary<Type, Func<object>> proxyFactories = new();
        private readonly ConcurrentDictionary<Assembly, IReadOnlyDictionary<Type, Type>> proxyTypesByAssembly = new();

        // Properties.
        public bool DisableCreationWithProxyTypes { get; set; }

        // Methods.
        public object CreateInstance(
            Type type,
            params object[] constructorArguments)
        {
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(constructorArguments);

            /* Resolve the db context engine creating the proxy from the current execution
             * context. Proxies are always created inside a db operation: model deserializations
             * run inside repository accesses, pushing their handler on the execution context. */
            var dbContextEngine = DbExecutionContextHandler.TryGetCurrentDbContextEngine(executionContext)
                ?? throw new InvalidOperationException("Can't create a proxy model outside of a db context execution scope");

            // If creation of proxy models are disabled, create a simple model instance.
            /* Only entity models have generated proxies: lazy loading and change candidate
             * marking only apply to them. Any other model type creates as a plain instance. */
            if (DisableCreationWithProxyTypes ||
                !typeof(IEntityModel).IsAssignableFrom(type))
            {
                return Activator.CreateInstance(
                    type,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    constructorArguments,
                    null)!;
            }

            // Create the proxy instance from its generated type.
            var proxyType = GetProxyType(type);
            var proxyModel = constructorArguments.Length == 0 ?
                proxyFactories.GetOrAdd(proxyType, BuildProxyFactory)() :
                Activator.CreateInstance(
                    proxyType,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    constructorArguments,
                    null)!;

            /* Bind the proxy to the scope of the current operation: the source repository
             * identified by the operation - the repository reading root documents, or the
             * one resolved for the reference member, whose handler carries the db context
             * owning it (the child db context, for a cross db context source) - and the db
             * context tracking its changes. Every proxy materializes inside an operation
             * addressing a collection, so a missing source repository is a broken flow, not
             * a supported state: fail loudly instead of returning an instance unable to
             * save or lazy load. Models deserialized with the no cache serializer modifier
             * don't bind change tracking, keeping read only massive scans out of the unit
             * of work. */
            var sourceRepository = DbExecutionContextHandler.TryGetCurrentRepository(dbContextEngine.ExecutionContext)
                ?? throw new InvalidOperationException(
                    $"Can't create a proxy model of type {type.Name} outside of an operation on a repository");
            var dbContext = dbContextEngine.SerializerModifierAccessor.IsNoCacheEnabled ?
                null :
                DbExecutionContextHandler.TryGetCurrentDbContext(dbContextEngine.ExecutionContext);
            ((IProxyModel)proxyModel).BindProxy(dbContext, sourceRepository);

            return proxyModel;
        }

        public TModel CreateInstance<TModel>(params object[] constructorArguments) =>
            (TModel)CreateInstance(typeof(TModel), constructorArguments);

        public bool IsProxyType(Type type) =>
            typeof(IProxyModel).IsAssignableFrom(type);

        public Type PurgeProxyType(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);

            return IsProxyType(type) ?
                type.BaseType! :
                type;
        }

        // Helpers.
        private static Func<object> BuildProxyFactory(Type proxyType) =>
            Expression.Lambda<Func<object>>(Expression.New(proxyType)).Compile();

        private Type GetProxyType(Type modelType)
        {
            /* Proxy types are declared at assembly level by the proxy models source generator:
             * read the model type's assembly declarations once, and cache them. */
            var proxyTypes = proxyTypesByAssembly.GetOrAdd(
                modelType.Assembly,
                assembly => assembly.GetCustomAttributes<GeneratedProxyModelAttribute>()
                    .ToDictionary(attribute => attribute.ModelType, attribute => attribute.ProxyModelType));

            return proxyTypes.TryGetValue(modelType, out var proxyType) ?
                proxyType :
                throw new InvalidOperationException(
                    $"No generated proxy model exists for type {modelType.Name}: " +
                    "its assembly must reference the MongODM proxy models source generator");
        }
    }
}
