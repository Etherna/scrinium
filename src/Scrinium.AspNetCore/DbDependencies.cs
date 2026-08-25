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
using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.ExecContext;
using Etherna.Scrinium.Core.Options;
using Etherna.Scrinium.Core.ProxyModels;
using Etherna.Scrinium.Core.Repositories;
using Etherna.Scrinium.Core.Serialization.Mapping;
using Etherna.Scrinium.Core.Serialization.Modifiers;
using Etherna.Scrinium.Core.Utility;
using Microsoft.Extensions.Options;
using System;

namespace Etherna.Scrinium.AspNetCore
{
    public class DbDependencies : IDbDependencies
    {
        public DbDependencies(
            IBsonSerializerRegistry bsonSerializerRegistry,
            IDbMaintainer dbMaintainer,
            IDbMigrationManager dbContextMigrationManager,
            IDiscriminatorRegistry discriminatorRegistry,
            IExecutionContext executionContext,
            IMapRegistry mapRegistry,
            IOptions<ScriniumOptions> mongODMOptions,
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
            MapRegistry = mapRegistry;
            ScriniumOptions = mongODMOptions.Value;
            ProxyGenerator = proxyGenerator;
            RepositoryRegistry = repositoryRegistry;
            SerializerModifierAccessor = serializerModifierAccessor;
        }

        public IBsonSerializerRegistry BsonSerializerRegistry { get; }
        public IDbMaintainer DbMaintainer { get; }
        public IDbMigrationManager DbMigrationManager { get; }
        public IDiscriminatorRegistry DiscriminatorRegistry { get; }
        public IExecutionContext ExecutionContext { get; }
        public IMapRegistry MapRegistry { get; }
        public ScriniumOptions ScriniumOptions { get; }
        public IProxyGenerator ProxyGenerator { get; }
        public IRepositoryRegistry RepositoryRegistry { get; }
        public ISerializerModifierAccessor SerializerModifierAccessor { get; }
    }
}
