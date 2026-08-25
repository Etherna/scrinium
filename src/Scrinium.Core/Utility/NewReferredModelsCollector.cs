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
using Etherna.MongODM.Core.ExecContext;
using Etherna.MongODM.Core.Extensions;
using Etherna.MongODM.Core.Repositories;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Etherna.MongODM.Core.Utility
{
    /// <summary>
    /// Ambient collector of a new referred models discovery pass. While one is active on the
    /// execution context, reference serializers report each serialized entity model without id,
    /// with the source repository resolved for its reference member, so that the new models can
    /// be created before persisting the referencing document.
    /// </summary>
    internal sealed class NewReferredModelsCollector : IDisposable
    {
        // Consts.
        private const string CollectorKey = "NewReferredModelsCollector";

        // Fields.
        private readonly HashSet<object> collectedModelsSet = new(ReferenceEqualityComparer.Instance);
        private readonly List<(IEntityModel Model, IRepository? SourceRepository)> models = [];
        private readonly ICollection<NewReferredModelsCollector> requests;

        // Constructors and dispose.
        public NewReferredModelsCollector(IExecutionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            requests = context.GetOrAddItemsList<NewReferredModelsCollector>(CollectorKey);

            lock (((ICollection)requests).SyncRoot)
                requests.Add(this);
        }

        public void Dispose()
        {
            lock (((ICollection)requests).SyncRoot)
                requests.Remove(this);
        }

        // Properties.
        public IReadOnlyCollection<(IEntityModel Model, IRepository? SourceRepository)> Models => models;

        // Methods.
        public void Collect(IEntityModel model, IRepository? sourceRepository)
        {
            ArgumentNullException.ThrowIfNull(model);

            //the same instance can be referred by different members: collect it once
            if (collectedModelsSet.Add(model))
                models.Add((model, sourceRepository));
        }

        // Static methods.
        public static NewReferredModelsCollector? TryGetCurrent(IExecutionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            var requests = context.TryGetItemsList<NewReferredModelsCollector>(CollectorKey);
            if (requests is null)
                return null;

            //get the last with a stack system, like the db execution context handlers
            lock (((ICollection)requests).SyncRoot)
                return requests.Reverse().FirstOrDefault();
        }
    }
}
