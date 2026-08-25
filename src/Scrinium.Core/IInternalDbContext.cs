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
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.ProxyModels;
using Etherna.Scrinium.Core.Repositories;

namespace Etherna.Scrinium.Core
{
    /// <summary>
    /// The db context surface invoked only inside the library (serializers and repositories):
    /// model tracking and identity map bookkeeping. Extends <see cref="IProxyModelsDbContext"/>,
    /// since everything the proxy models invoke the internals invoke too. Implemented
    /// explicitly by <see cref="DbContext"/>.
    /// </summary>
    internal interface IInternalDbContext : IProxyModelsDbContext
    {
        // Methods.
        /// <summary>
        /// Remove a model from the change candidates of this db context instance, after its
        /// changes have been saved. Its model document is kept, so following mutations are tracked.
        /// </summary>
        /// <param name="model">The model to clear</param>
        void ClearChangeCandidate(IEntityModel model);

        /// <summary>
        /// Register a model instance as the loaded one for its document on this db context
        /// instance. Following loads of the same document will return the same instance.
        /// </summary>
        /// <param name="modelId">The model document id</param>
        /// <param name="model">The loaded model instance</param>
        void RegisterLoadedModel(object modelId, IEntityModel model);

        /// <summary>
        /// Remove a model from the change tracking of this db context instance, dropping its
        /// model document and its change candidate flag, keeping it out of the next changes save.
        /// </summary>
        /// <param name="model">The model to remove</param>
        void RemoveModelTracking(IEntityModel model);

        /// <summary>
        /// Replace the loaded model instance of a document with a fresh one carrying the
        /// current document type, invoked by the load deduplication when a full load finds
        /// the document with another type of its hierarchy. The outdated instance leaves the
        /// change tracking and starts denying any application interaction, throwing
        /// <see cref="Exceptions.ScriniumOutdatedModelTypeException"/>.
        /// </summary>
        /// <param name="modelId">The model document id</param>
        /// <param name="outdatedModel">The loaded instance with the outdated type</param>
        /// <param name="currentModel">The fresh instance with the current document type</param>
        void ReplaceOutdatedLoadedModel(object modelId, IEntityModel outdatedModel, IEntityModel currentModel);

        /// <summary>
        /// Set the model document of a model on this db context instance: the serialized
        /// form its loaded members are diffed against at save. Captured at load and
        /// create, and refreshed after each save.
        /// </summary>
        /// <param name="model">The tracked model</param>
        /// <param name="bsonDocument">The serialized model document, diffed against at save</param>
        void SetModelBsonDocument(IEntityModel model, BsonDocument bsonDocument);

        /// <summary>
        /// Bind a model to its source repository on this db context instance, for a tracked
        /// model that can't carry it (a created or replaced non proxy instance), so its changes
        /// save to the right repository even when the model type is handled by many repositories.
        /// </summary>
        /// <param name="model">The tracked model</param>
        /// <param name="sourceRepository">The model source repository</param>
        void SetModelSourceRepository(IEntityModel model, IRepository sourceRepository);

        /// <summary>
        /// Try to get the model document of a model on this db context instance.
        /// </summary>
        /// <param name="model">The tracked model</param>
        /// <returns>The model document, or null when the model is not tracked</returns>
        BsonDocument? TryGetModelBsonDocument(IEntityModel model);
    }
}
