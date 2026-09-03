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
using System;

namespace Etherna.Scrinium.Core.Serialization.Mapping
{
    /// <summary>
    /// Builder surface of the model maps declared by a reference serializer configuration.
    /// Reference documents are denormalized summaries of their origin document: a post-load fix
    /// function belongs to the root model map schemas of the origin document, so reference
    /// schemas can't declare one.
    /// </summary>
    public interface IReferenceModelMapBuilder<TModel>
    {
        // Methods.
        /// <summary>
        /// Add a fallback serializer invoked in case of unrecognized schema id
        /// </summary>
        /// <param name="fallbackSerializer">Fallback serializer</param>
        /// <returns>This same model map</returns>
        IReferenceModelMapBuilder<TModel> AddFallbackCustomSerializer(
            IBsonSerializer<TModel> fallbackSerializer);

        /// <summary>
        /// Add a fallback model map invoked in case of unrecognized schema id, and absence of fallback serializer
        /// </summary>
        /// <param name="modelMapSchemaInitializer">The model map inizializer</param>
        /// <param name="baseSchemaId">Id of the base model map for this model map</param>
        /// <returns>This same model map</returns>
        IReferenceModelMapBuilder<TModel> AddFallbackSchema(
            Action<BsonClassMap<TModel>>? modelMapSchemaInitializer = null,
            string? baseSchemaId = null);

        /// <summary>
        /// Register a secondary model map schema
        /// </summary>
        /// <param name="id">The map Id</param>
        /// <param name="modelMapSchemaInitializer">The model map schema inizializer</param>
        /// <param name="baseSchemaId">Id of the base model map schema for this model map schema</param>
        /// <returns>This same model map</returns>
        IReferenceModelMapBuilder<TModel> AddSecondarySchema(
            string id,
            Action<BsonClassMap<TModel>>? modelMapSchemaInitializer = null,
            string? baseSchemaId = null);

        IReferenceModelMapBuilder<TModel> AddSecondarySchema<TOverrideNominal>(
            string id,
            Action<BsonClassMap<TOverrideNominal>>? modelMapSchemaInitializer = null,
            string? baseSchemaId = null)
            where TOverrideNominal : class, TModel;
    }
}
