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

using Etherna.ExecContext;
using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace Etherna.MongODM.Core.Utility
{
    public class DbCache : IDbCache
    {
        // Consts.
        private const string CacheKeyPrefix = "DBCache-";

        // Fields.
        private string cacheKey = default!;
        private IExecutionContext executionContext = default!;
        private ILogger logger = default!;

        // Constructors.
        public void Initialize(IDbContext dbContext, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(dbContext);
            if (IsInitialized)
                throw new InvalidOperationException("Instance already initialized");

            var cacheKeyBuilder = new StringBuilder(CacheKeyPrefix);
            cacheKeyBuilder.Append(dbContext.Identifier);
            cacheKey = cacheKeyBuilder.ToString();
            executionContext = dbContext.ExecutionContext;
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

            IsInitialized = true;

            this.logger.DbCacheInitialized(dbContext.Options.DbName);
        }

        // Properties.
        public bool IsInitialized { get; private set; }
        public IReadOnlyDictionary<object, IEntityModel> LoadedModels
        {
            get
            {
                if (executionContext.Items is null)
                    throw new InvalidOperationException("Execution context can't have null Items here");

                lock (executionContext.Items)
                    return GetScopedCache();
            }
        }

        // Methods.
        public void ClearCache()
        {
            if (executionContext.Items is null)
                throw new InvalidOperationException("Execution context can't have null Items here");

            lock (executionContext.Items)
                GetScopedCache().Clear();
        }

        public void AddModel<TModel>(object id, TModel model)
            where TModel : class, IEntityModel
        {
            ArgumentNullException.ThrowIfNull(id);
            ArgumentNullException.ThrowIfNull(model);
            if (executionContext.Items is null)
                throw new InvalidOperationException("Execution context can't have null Items here");

            lock (executionContext.Items)
                GetScopedCache().Add(id, model);
        }

        public void RemoveModel(object id)
        {
            if (executionContext.Items is null)
                throw new InvalidOperationException("Execution context can't have null Items here");

            lock (executionContext.Items)
                GetScopedCache().Remove(id);
        }

        // Helpers.
        private Dictionary<object, IEntityModel> GetScopedCache()
        {
            if (executionContext.Items is null)
                throw new InvalidOperationException("Execution context can't have null Items here");

            if (!executionContext.Items.ContainsKey(cacheKey))
                executionContext.Items.Add(cacheKey, new Dictionary<object, IEntityModel>());

            return (Dictionary<object, IEntityModel>)executionContext.Items[cacheKey]!;
        }
    }
}
