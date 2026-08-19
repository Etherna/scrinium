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
using Etherna.MongODM.Core.Repositories;
using System.Collections.Generic;
using System.Reflection;

namespace Etherna.MongODM.Core.Utility
{
    /// <summary>
    /// Interface for <see cref="DbMaintainer"/> implementation.
    /// </summary>
    public interface IDbMaintainer : IDbContextEngineInitializable
    {
        // Methods.
        /// <summary>
        /// Method to invoke when a model is deleted through its repository, to propagate the
        /// delete to the documents referencing it, applying the origin delete policy each
        /// reference declares.
        /// </summary>
        /// <typeparam name="TKey">Deleted model Key type</typeparam>
        /// <param name="deletedModel">The deleted model</param>
        /// <param name="referenceRepository">The repository of the deleted model</param>
        void OnDeletedModel<TKey>(IEntityModel deletedModel, IRepository referenceRepository);

        /// <summary>
        /// Method to invoke when a tracked model is updated, to propagate its changes to the
        /// summaries of the documents referencing it.
        /// </summary>
        /// <typeparam name="TKey">Updated model Key type</typeparam>
        /// <param name="updatedModel">The updated model</param>
        /// <param name="changedMembers">The updated model changed members</param>
        /// <param name="referenceRepository">The repository of the updated model</param>
        void OnUpdatedModel<TKey>(IEntityModel updatedModel, IEnumerable<MemberInfo> changedMembers, IRepository referenceRepository);
    }
}