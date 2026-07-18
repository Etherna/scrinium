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

using Etherna.MongoDB.Bson.Serialization;
using Etherna.MongODM.Core.Repositories;
using System;

namespace Etherna.MongODM.Core.Serialization.Serializers
{
    public interface IReferenceSerializer :
        IBsonDocumentSerializer,
        IBsonIdProvider,
        IModelMapsHandlingSerializer
    {
        // Properties.
        ReferenceSerializerConfiguration Configuration { get; }

        // Internal properties.
        internal Type ReferenceKeyType { get; }
        internal Type ReferenceModelType { get; }

        /// <summary>
        /// The source repository selector: declared on construction, or resolved by the
        /// map registry at engine build. Null for references to models of another db context.
        /// </summary>
        internal Func<IDbContext, IRepository>? SourceRepositorySelector { get; set; }
    }
}