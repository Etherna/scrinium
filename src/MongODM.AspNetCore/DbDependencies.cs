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
using Etherna.MongODM.Core;
using Etherna.MongODM.Core.ExecContext;
using Etherna.MongODM.Core.Options;
using Etherna.MongODM.Core.ProxyModels;
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.Core.Serialization.Mapping;
using Etherna.MongODM.Core.Serialization.Modifiers;
using Etherna.MongODM.Core.Utility;
using Microsoft.Extensions.Options;
using System;

namespace Etherna.MongODM.AspNetCore
{
    public class DbDependencies : IDbDependencies
    {
        public DbDependencies(
            IBsonSerializerRegistry bsonSerializerRegistry,
            IDbMaintainer dbMaintainer,
            IDbMigrationManager dbContextMigrationManager,
            IDiscriminatorRegistry discriminatorRegistry,
            IExecutionContext executionContext,
            ILoadedModelsTracker loadedModelsTracker,
            IMapRegistry mapRegistry,
            IOptions<MongODMOptions> mongODMOptions,
            IProxyGenerator proxyGenerator,
            IRepositoryRegistry repositoryRegistry,
            ISerializerModifierAccessor serializerModifierAccessor)
        {
            ArgumentNullException.ThrowIfNull(mongODMOptions);
            BsonSerializerRegistry = bsonSerializerRegistry;
            DbMaintainer = dbMaintainer;
            DbMigrationManager = dbContextMigrationManager;
            DiscriminatorRegistry = discriminatorRegistry;
            ExecutionContext = executionContext;
            LoadedModelsTracker = loadedModelsTracker;
            MapRegistry = mapRegistry;
            MongODMOptions = mongODMOptions.Value;
            ProxyGenerator = proxyGenerator;
            RepositoryRegistry = repositoryRegistry;
            SerializerModifierAccessor = serializerModifierAccessor;
        }

        public IBsonSerializerRegistry BsonSerializerRegistry { get; }
        public IDbMaintainer DbMaintainer { get; }
        public IDbMigrationManager DbMigrationManager { get; }
        public IDiscriminatorRegistry DiscriminatorRegistry { get; }
        public IExecutionContext ExecutionContext { get; }
        public ILoadedModelsTracker LoadedModelsTracker { get; }
        public IMapRegistry MapRegistry { get; }
        public MongODMOptions MongODMOptions { get; }
        public IProxyGenerator ProxyGenerator { get; }
        public IRepositoryRegistry RepositoryRegistry { get; }
        public ISerializerModifierAccessor SerializerModifierAccessor { get; }
    }
}
