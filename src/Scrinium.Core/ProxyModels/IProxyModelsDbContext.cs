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

using Etherna.MongODM.Core.Domain.Models;
using System;

namespace Etherna.MongODM.Core.ProxyModels
{
    /// <summary>
    /// The db context surface invoked by the generated proxy models: change tracking
    /// signals and load reaction hooks. Implemented explicitly by <see cref="DbContext"/>;
    /// public only because the proxies are emitted into the consumer assemblies.
    /// Infrastructure interface, not intended for application use.
    /// </summary>
    public interface IProxyModelsDbContext
    {
        // Properties.
        /// <summary>
        /// True while change tracking is suppressed on this db context instance (see
        /// <see cref="SuppressChangeTracking"/>): the library internals are reading models
        /// to merge or diff loaded data.
        /// </summary>
        bool IsChangeTrackingSuppressed { get; }

        // Methods.
        /// <summary>
        /// Flag a proxy model as a change candidate on this db context instance, invoked by
        /// change tracking on a mutation. The mark is ignored until the model has a model document
        /// (skipping the deserialization sets) and while change tracking is suppressed.
        /// </summary>
        /// <param name="model">The mutated model</param>
        void MarkChangeCandidate(IEntityModel model);

        /// <summary>
        /// React to an implicit lazy load, before it runs, honoring
        /// <see cref="Options.IDbContextOptions.ImplicitLazyLoad"/>: log a warning once per
        /// member per scope, stay silent, or deny the load throwing
        /// <see cref="Exceptions.MongodmLazyLoadingException"/>. Invoked by the proxy models.
        /// </summary>
        /// <param name="modelType">The summary model type</param>
        /// <param name="memberName">The read member, null for an unanalyzed domain method</param>
        void OnImplicitLazyLoad(Type modelType, string? memberName);

        /// <summary>
        /// React to a full load finding no origin document for a summary model, honoring the
        /// <see cref="Options.ReactionMode"/> declared by the reference that
        /// deserialized it: log a warning once per model type and source repository per scope,
        /// stay silent, or report the db inconsistency throwing
        /// <see cref="Exceptions.MongodmMissingOriginDocumentException"/>. Invoked by the proxy
        /// models and by the explicit preloads.
        /// </summary>
        /// <param name="summaryModel">The summary model whose origin document is missing</param>
        void OnMissingOriginDocument(IEntityModel summaryModel);

        /// <summary>
        /// Suppress change tracking on this db context instance until the returned scope is
        /// disposed: mutations don't flag change candidates. Used while merging loaded data
        /// into a model, keeping the merge out of the unit of work.
        /// </summary>
        /// <returns>The suppression scope</returns>
        IDisposable SuppressChangeTracking();
    }
}
