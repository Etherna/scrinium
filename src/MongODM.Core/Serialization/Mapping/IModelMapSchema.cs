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
using Etherna.MongODM.Core.Utility;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core.Serialization.Mapping
{
    public interface IModelMapSchema : IFreezableConfig
    {
        // Properties.
        string Id { get; }
        ReadOnlyCollection<BsonMemberMap> AllMemberMaps { get; }
        IModelMapSchema? BaseSchema { get; }
        string? BaseSchemaId { get; }
        string Discriminator { get; }
        bool DiscriminatorIsRequired { get; }
        IEnumerable<IMemberMap> GeneratedMemberMaps { get; }
        bool HasRootClass { get; }
        IMemberMap? IdMemberMap { get; }
        bool IsCurrentActive { get; }
        bool IsEntity { get; }
        bool IsRootClass { get; }
        IModelMap ModelMap { get; }
        Type ModelType { get; }
        IBsonSerializer Serializer { get; }

        // Methods.
        Task<object> FixDeserializedModelAsync(object model);
        void SetBaseModelMapSchema(IModelMapSchema baseModelMapSchema);
        BsonMemberMap? TryGetMemberMap(string memberName);
        bool TryUseProxyGenerator(IDbContextEngine dbContextEngine);
        void UseProxyGenerator(IDbContextEngine dbContextEngine);
    }

    public interface IModelMapSchema<TModel> : IModelMapSchema
    {
        // Methods.
        Task<TModel> FixDeserializedModelAsync(TModel model);
    }
}