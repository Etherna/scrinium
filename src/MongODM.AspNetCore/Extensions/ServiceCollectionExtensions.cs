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
using Etherna.MongODM.AspNetCore.ExecContext;
using Etherna.MongODM.Core;
using Etherna.MongODM.Core.Conventions;
using Etherna.MongODM.Core.ExecContext;
using Etherna.MongODM.Core.ExecContext.AsyncLocal;
using Etherna.MongODM.Core.Options;
using Etherna.MongODM.Core.ProxyModels;
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.Core.Serialization.Mapping;
using Etherna.MongODM.Core.Serialization.Modifiers;
using Etherna.MongODM.Core.Tasks;
using Etherna.MongODM.Core.Utility;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;

namespace Etherna.MongODM.AspNetCore.Extensions
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

        public static IMongODMConfiguration AddMongODM<TTaskRunner>(
            this IServiceCollection services,
            Action<MongODMOptions>? configureOptions = null)
            where TTaskRunner : class, ITaskRunner, ITaskRunnerBuilder
        {
            // MongODM generic configuration.
            var configuration = new MongODMConfiguration(services);

            services.AddOptions<MongODMOptions>()
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
                    taskRunnerBuilder.SetMongODMOptions(options);
                });

            services.AddExecutionContext();

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
