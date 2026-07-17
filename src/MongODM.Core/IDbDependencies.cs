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
using Etherna.MongODM.Core.ExecContext;
using Etherna.MongODM.Core.ProxyModels;
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.Core.Serialization.Mapping;
using Etherna.MongODM.Core.Serialization.Modifiers;
using Etherna.MongODM.Core.Utility;

namespace Etherna.MongODM.Core
{
    public interface IDbDependencies
    {
        IBsonSerializerRegistry BsonSerializerRegistry { get; }
        IDbMaintainer DbMaintainer { get; }
        IDbMigrationManager DbMigrationManager { get; }
        IDiscriminatorRegistry DiscriminatorRegistry { get; }
        IExecutionContext ExecutionContext { get; }
        IMapRegistry MapRegistry { get; }
        IProxyGenerator ProxyGenerator { get; }
        IRepositoryRegistry RepositoryRegistry { get; }
        ISerializerModifierAccessor SerializerModifierAccessor { get; }
    }
}