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
using Etherna.MongoDB.Bson.Serialization.Serializers;
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Serialization.Mapping;
using System;
using System.Collections.Generic;

namespace Etherna.Scrinium.Core.Serialization.Serializers
{
    public class EntityModelSerializerAdapter<TExternalModel, TInternalModel, TKey>(
        IBsonSerializer<TInternalModel> serializerBase) :
        SerializerBase<TExternalModel>,
        IBsonDocumentSerializer,
        IBsonIdProvider,
        IEntityModelSerializerAdapter,
        IModelMapsHandlingSerializer
        where TInternalModel : class, IEntityModel<TKey>
        where TExternalModel : class, IEntityModel<TKey>
    {
        // Properties.
        public IEnumerable<IModelMap> HandledModelMaps => (serializerBase as IModelMapsHandlingSerializer)?.HandledModelMaps ?? Array.Empty<IModelMap>();

        // Methods.
        public override TExternalModel Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args) =>
            (serializerBase.Deserialize(context, args) as TExternalModel)!;

        public bool GetDocumentId(object document, out object id, out Type idNominalType, out IIdGenerator idGenerator)
        {
            if (serializerBase is IBsonIdProvider idProviderSerializerBase)
                return idProviderSerializerBase.GetDocumentId(document, out id, out idNominalType, out idGenerator);

            id = null!;
            idNominalType = null!;
            idGenerator = null!;
            return false;
        }

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, TExternalModel value)
        {
            serializerBase.Serialize(context, args, value);
        }

        public void SetDocumentId(object document, object id)
        {
            if (serializerBase is IBsonIdProvider idProviderSerializerBase)
                idProviderSerializerBase.SetDocumentId(document, id);
        }

        public bool TryGetMemberSerializationInfo(string memberName, out BsonSerializationInfo serializationInfo)
        {
            if (serializerBase is IBsonDocumentSerializer documentSerializerBase)
                return documentSerializerBase.TryGetMemberSerializationInfo(memberName, out serializationInfo);

            serializationInfo = null!;
            return false;
        }

        // Internals.
        IBsonSerializer IEntityModelSerializerAdapter.SerializerBase => serializerBase;
    }
}
