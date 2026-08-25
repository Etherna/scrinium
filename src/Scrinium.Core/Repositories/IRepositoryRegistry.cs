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
using System.Collections.Generic;

namespace Etherna.MongODM.Core.Repositories
{
    public interface IRepositoryRegistry : IDbContextInitializable
    {
        // Properties.
        IEnumerable<IRepository> Repositories { get; }

        // Methods.
        /// <summary>
        /// Get repository that have a specific entity model type as base
        /// </summary>
        /// <typeparam name="TModel">Base model type to search</typeparam>
        /// <typeparam name="TKey">Key type of base model</typeparam>
        /// <returns>Found repository</returns>
        IRepository<TModel, TKey> GetRepositoryByBaseModelType<TModel, TKey>()
            where TModel : class, IEntityModel<TKey>;

        /// <summary>
        /// Get repository that can handle a specific entity model type
        /// </summary>
        /// <param name="modelType">Model type to search</param>
        /// <returns>Entity model handling repository</returns>
        IRepository GetRepositoryByHandledModelType(Type modelType);

        /// <summary>
        /// Try to get repository that can handle a specific entity model type
        /// </summary>
        /// <param name="modelType">Model type to search</param>
        /// <returns>Entity model handling repository. Null if doesn't exist</returns>
        IRepository? TryGetRepositoryByHandledModelType(Type modelType);
    }
}