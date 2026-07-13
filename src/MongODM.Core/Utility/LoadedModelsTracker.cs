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
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace Etherna.MongODM.Core.Utility
{
    public class LoadedModelsTracker : ILoadedModelsTracker
    {
        // Consts.
        private const string TrackerKeyPrefix = "LoadedModelsTracker-";

        // Fields.
        private IExecutionContext executionContext = default!;
        private ILogger logger = default!;
        private string trackerKey = default!;

        // Constructors.
        public void Initialize(IDbContext dbContext, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(dbContext);
            if (IsInitialized)
                throw new InvalidOperationException("Instance already initialized");

            var trackerKeyBuilder = new StringBuilder(TrackerKeyPrefix);
            trackerKeyBuilder.Append(dbContext.Identifier);
            trackerKey = trackerKeyBuilder.ToString();
            executionContext = dbContext.ExecutionContext;
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

            IsInitialized = true;

            this.logger.LoadedModelsTrackerInitialized(dbContext.Options.DbName);
        }

        // Properties.
        public bool IsInitialized { get; private set; }
        public IReadOnlyCollection<IEntityModel> LoadedModels
        {
            get
            {
                if (executionContext.Items is null)
                    throw new InvalidOperationException("Execution context can't have null Items here");

                lock (executionContext.Items)
                    return GetScopedModels().ToArray();
            }
        }

        // Methods.
        public void ClearTracked()
        {
            if (executionContext.Items is null)
                throw new InvalidOperationException("Execution context can't have null Items here");

            lock (executionContext.Items)
                GetScopedModels().Clear();
        }

        public void TrackModel(IEntityModel model)
        {
            ArgumentNullException.ThrowIfNull(model);
            if (executionContext.Items is null)
                throw new InvalidOperationException("Execution context can't have null Items here");

            lock (executionContext.Items)
                GetScopedModels().Add(model);
        }

        public void UntrackModel(IEntityModel model)
        {
            ArgumentNullException.ThrowIfNull(model);
            if (executionContext.Items is null)
                throw new InvalidOperationException("Execution context can't have null Items here");

            lock (executionContext.Items)
                GetScopedModels().Remove(model);
        }

        // Helpers.
        private List<IEntityModel> GetScopedModels()
        {
            if (executionContext.Items is null)
                throw new InvalidOperationException("Execution context can't have null Items here");

            if (!executionContext.Items.ContainsKey(trackerKey))
                executionContext.Items.Add(trackerKey, new List<IEntityModel>());

            return (List<IEntityModel>)executionContext.Items[trackerKey]!;
        }
    }
}
