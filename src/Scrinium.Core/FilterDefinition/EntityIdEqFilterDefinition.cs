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
using Etherna.MongoDB.Driver;
using Etherna.Scrinium.Core.Domain.Models;
using System;

namespace Etherna.Scrinium.Core.FilterDefinition
{
    /// <summary>
    /// The id equality filter of the repository operations addressing a single document by its
    /// key. Renders as the driver eq filter on the entity id member, refusing with a
    /// <see cref="FormatException"/> an id value serialized to a document or to an array: an
    /// entity id is always a value, and a document valued id would be read by MongoDB as an
    /// operator expression, turning the id equality into an arbitrary query.
    /// </summary>
    public class EntityIdEqFilterDefinition<TModel, TKey>(TKey id) : FilterDefinition<TModel>
        where TModel : IEntityModel<TKey>
    {
        // Methods.
        public override BsonDocument Render(RenderArgs<TModel> args)
        {
            var renderedFilter = Builders<TModel>.Filter.Eq(m => m.Id, id).Render(args);
            IdFilterValueHelper.ThrowIfNotValueShaped(renderedFilter.GetElement(0));
            return renderedFilter;
        }
    }
}
