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
using Etherna.MongoDB.Bson.Serialization.Conventions;
using Etherna.Scrinium.AspNetCore.ExecContext;
using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.Conventions;
using Etherna.Scrinium.Core.ExecContext;
using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Etherna.Scrinium.Core.Options;
using Etherna.Scrinium.Core.ProxyModels;
using Etherna.Scrinium.Core.Repositories;
using Etherna.Scrinium.Core.Serialization.Mapping;
using Etherna.Scrinium.Core.Serialization.Modifiers;
using Etherna.Scrinium.Core.Tasks;
using Etherna.Scrinium.Core.Utility;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;

namespace Etherna.Scrinium.AspNetCore.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddExecutionContext(
            this IServiceCollection services)
        {
            services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            services.TryAddSingleton<IExecutionContext>(serviceProvider =>
               new ExecutionContextSelector( //default
               [
                   new HttpContextExecutionContext(serviceProvider.GetRequiredService<IHttpContextAccessor>()),
                   AsyncLocalContext.Instance
               ]));

            return services;
        }

        public static IScriniumConfiguration AddScrinium<TTaskRunner>(
            this IServiceCollection services,
            Action<ScriniumOptions>? configureOptions = null)
            where TTaskRunner : class, ITaskRunner, ITaskRunnerBuilder
        {
            // MongODM generic configuration.
            var configuration = new ScriniumConfiguration(services);

            services.AddOptions<ScriniumOptions>()
                .Configure(configureOptions ?? (_ => { }))
                .PostConfigure<ITaskRunnerBuilder>(
                (options, taskRunnerBuilder) =>
                {
                    // Register global conventions.
                    /* The driver convention registry is process wide: the pack applies only to
                     * the class maps built while a db context engine registers its maps, so the
                     * enum representation of any other type automapped in the process, by another
                     * consumer of the driver, keeps the driver default. */
                    ConventionRegistry.Register("Enum string", new ConventionPack
                    {
                        new EnumRepresentationConvention(BsonType.String)
                    }, _ => MapsRegistrationHandler.IsRegisteringMaps(DeferredExecutionContext.Instance));

                    // Freeze configuration into mongodm options.
                    configuration.Freeze(options);

                    // Link options to services.
                    taskRunnerBuilder.SetScriniumOptions(options);
                });

            services.AddExecutionContext();

            services.TryAddSingleton<IParentEnginesProvider, ParentEnginesProvider>();
            services.TryAddSingleton<IProxyGenerator, ProxyGenerator>();
            services.TryAddSingleton<ITaskRunner, TTaskRunner>();
            services.TryAddSingleton<ITaskRunnerBuilder>(sp => (TTaskRunner)sp.GetRequiredService<ITaskRunner>());

            /* Register discriminator convention on typeof(object) because we need a method to handle
             * default returned instance from static calls to BsonSerializer.LookupDiscriminatorConvention(Type).
             * Several points internal to drivers invoke this method, and we can't avoid it. We need to set the default.
             */
            BsonSerializer.RegisterDiscriminatorConvention(typeof(object),
                new HierarchicalProxyTolerantDiscriminatorConvention("_t", DeferredExecutionContext.Instance));

            /* For same reason of handle static calls to BsonSerializer.LookupSerializer(Type),
             * we need a way to inject a current context accessor. This is a modification on official drivers,
             * waiting an official implementation of serialization contexts.
             */
            BsonSerializer.SetSerializationContextAccessor(
                new SerializationContextAccessor(DeferredExecutionContext.Instance));

            // DbContext internal.
            //dependencies
            /*****
             * Transient dependencies have to be injected only into DbContext instance,
             * and passed to other with Initialize() method. This because otherwise inside
             * the same dbContext different components could have different instances of the same component.
             */
            services.TryAddTransient<IBsonSerializerRegistry, BsonSerializerRegistry>();
            services.TryAddTransient<IDbDependencies, DbDependencies>();
            services.TryAddTransient<IDbMaintainer, DbMaintainer>();
            services.TryAddTransient<IDbMigrationManager, DbMigrationManager>();
            services.TryAddTransient<IDiscriminatorRegistry, DiscriminatorRegistry>();
            services.TryAddTransient<IMapRegistry, MapRegistry>();
            services.TryAddTransient<IRepositoryRegistry, RepositoryRegistry>();
            services.TryAddSingleton<ISerializerModifierAccessor, SerializerModifierAccessor>();

            //tasks
            services.TryAddTransient<IDeleteDocDependenciesTask, DeleteDocDependenciesTask>();
            services.TryAddTransient<IMigrateDbContextTask, MigrateDbContextTask>();
            services.TryAddTransient<IUpdateDocDependenciesTask, UpdateDocDependenciesTask>();

            return configuration;
        }
    }
}
