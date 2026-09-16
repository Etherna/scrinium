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
    /// The builder surface of a model map registered by a reference serializer configuration.
    /// It wraps the model map instead of being implemented by it, so that the root builder
    /// surface stays unreachable from a reference configuration, a cast included: the post-load
    /// fix function its schemas declare runs on the root deserialization path only, and a model
    /// fix belongs to the origin document's root schemas.
    /// </summary>
    internal sealed class ReferenceModelMapBuilder<TModel>(ModelMap<TModel> modelMap)
        : IReferenceModelMapBuilder<TModel>
    {
        // Methods.
        public IReferenceModelMapBuilder<TModel> AddFallbackCustomSerializer(
            IBsonSerializer<TModel> fallbackSerializer)
        {
            modelMap.AddFallbackCustomSerializer(fallbackSerializer);
            return this;
        }

        public IReferenceModelMapBuilder<TModel> AddFallbackSchema(
            Action<BsonClassMap<TModel>>? modelMapSchemaInitializer = null,
            string? baseSchemaId = null)
        {
            modelMap.AddFallbackSchema(modelMapSchemaInitializer, baseSchemaId);
            return this;
        }

        public IReferenceModelMapBuilder<TModel> AddSecondarySchema(
            string id,
            Action<BsonClassMap<TModel>>? modelMapSchemaInitializer = null,
            string? baseSchemaId = null)
        {
            modelMap.AddSecondarySchema(id, modelMapSchemaInitializer, baseSchemaId);
            return this;
        }

        public IReferenceModelMapBuilder<TModel> AddSecondarySchema<TOverrideNominal>(
            string id,
            Action<BsonClassMap<TOverrideNominal>>? modelMapSchemaInitializer = null,
            string? baseSchemaId = null)
            where TOverrideNominal : class, TModel
        {
            modelMap.AddSecondarySchema<TOverrideNominal>(id, modelMapSchemaInitializer, baseSchemaId);
            return this;
        }
    }
}
