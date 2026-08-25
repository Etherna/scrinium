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

using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Exceptions;
using Etherna.Scrinium.Core.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Etherna.Scrinium.Core.Repositories
{
    public class RepositoryRegistry : IRepositoryRegistry
    {
        // Fields.
        private ILogger logger = null!;
        private Dictionary<Type, IRepository[]> _repositoriesByModelType = null!;

        // Initializer.
        public void Initialize(IDbContext dbContext, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(dbContext);
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (IsInitialized)
                throw new InvalidOperationException("Instance already initialized");

            // Initialize repository dictionary.
            //select IRepository<,> from dbcontext properties
            var dbContextType = dbContext.GetType();
            var repos = dbContextType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                .Where(prop =>
                {
                    var propType = prop.PropertyType;

                    if (propType.IsGenericType &&
                        propType.GetGenericTypeDefinition() == typeof(IRepository<,>))
                        return true;

                    if (propType.GetInterfaces()
                                .Where(@interface => @interface.IsGenericType)
                                .Select(@interface => @interface.GetGenericTypeDefinition())
                                .Contains(typeof(IRepository<,>)))
                        return true;

                    return false;
                });

            //initialize registry
            _repositoriesByModelType = repos
                .Select(prop => (IRepository)prop.GetValue(dbContext)!)
                .GroupBy(repository => repository.ModelType)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToArray());

            IsInitialized = true;

            this.logger.RepositoryRegistryInitialized(dbContext.Engine.Options.DbName);
        }

        // Properties.
        public bool IsInitialized { get; private set; }
        public IEnumerable<IRepository> Repositories => _repositoriesByModelType.Values.SelectMany(repositories => repositories);

        // Methods.
        public IRepository<TModel, TKey> GetRepositoryByBaseModelType<TModel, TKey>()
            where TModel : class, IEntityModel<TKey> =>
            (IRepository<TModel, TKey>)GetRepositoryByHandledModelType(typeof(TModel));

        public IRepository GetRepositoryByHandledModelType(Type modelType)
        {
            ArgumentNullException.ThrowIfNull(modelType);
            
            while (!_repositoriesByModelType.ContainsKey(modelType))
            {
                if (modelType == typeof(object))
                    throw new InvalidOperationException($"Cant find valid repository for model type {modelType}");
                modelType = modelType.BaseType!;
            }

            var repositories = _repositoriesByModelType[modelType];
            if (repositories.Length > 1)
                throw new MongodmAmbiguousRepositoryException(
                    $"Multiple repositories handle model type {modelType}: identify the origin repository explicitly");

            return repositories[0];
        }

        public IRepository? TryGetRepositoryByHandledModelType(Type modelType)
        {
            try
            {
                return GetRepositoryByHandledModelType(modelType);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }
}
