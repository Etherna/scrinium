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
using Etherna.MongODM.Core.Utility;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Etherna.MongODM.Core.Serialization.Mapping
{
    /// <summary>
    /// Interface for <see cref="MapRegistry"/> implementation.
    /// </summary>
    public interface IMapRegistry : IDbContextEngineInitializable, IFreezableConfig
    {
        // Properties.
        /// <summary>
        /// All registered maps, indexed by model type
        /// </summary>
        IReadOnlyDictionary<Type, IMap> MapsByModelType { get; }

        IReadOnlyDictionary<string, IMemberMap> MemberMapsById { get; }

        // Methods.
        /// <summary>
        /// Register a new map based on custom serializer
        /// </summary>
        /// <typeparam name="TModel">The model type</typeparam>
        /// <param name="customSerializer">Custom serializer</param>
        /// <returns>The new schema</returns>
        ICustomSerializerMapBuilder<TModel> AddCustomSerializerMap<TModel>(
            IBsonSerializer<TModel> customSerializer);

        /// <summary>
        /// Register a new schema based on model map
        /// </summary>
        /// <typeparam name="TModel">The model type</typeparam>
        /// <param name="activeModelMapSchemaId">The active model map schema Id</param>
        /// <param name="activeModelMapSchemaInitializer">The active model map schema inizializer</param>
        /// <returns>The new model map</returns>
        IModelMapBuilder<TModel> AddModelMap<TModel>(
            string activeModelMapSchemaId,
            Action<BsonClassMap<TModel>>? activeModelMapSchemaInitializer = null);

        /// <summary>
        /// Return bson element for represent a model map id
        /// </summary>
        /// <param name="modelType">The model type</param>
        /// <returns>The model map id bson element</returns>
        BsonElement GetActiveModelMapIdBsonElement(Type modelType);

        IBsonSerializer GetMappedSerializer(Type modelType);

        /// <summary>
        /// Get all member maps that points to a specific member definition
        /// </summary>
        /// <param name="memberInfo">The member definition</param>
        /// <returns>List of member maps</returns>
        IEnumerable<IMemberMap> GetMemberMapsFromMemberInfo(MemberInfo memberInfo);

        IEnumerable<IMemberMap> GetMemberMapsWithSameElementPath(IMemberMap memberMap);

        /// <summary>
        /// Get a registered model map for a given model type
        /// </summary>
        /// <param name="modelType">The model type</param>
        /// <returns>The registered model schema</returns>
        IModelMap GetModelMap(Type modelType);

        bool TryGetMappedSerializer(Type modelType, out IBsonSerializer serializer);

        /// <summary>
        /// Try to get a registered model map for a given model type
        /// </summary>
        /// <param name="modelType">The model type</param>
        /// <param name="modelMap">Output model map, if exists</param>
        /// <returns>Operation result</returns>
        bool TryGetModelMap(Type modelType, out IModelMap modelMap);
    }
}