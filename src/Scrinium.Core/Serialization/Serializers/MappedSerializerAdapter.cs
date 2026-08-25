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
using Etherna.MongODM.Core.Serialization.Mapping;
using System;
using System.Collections.Generic;

namespace Etherna.MongODM.Core.Serialization.Serializers
{
    /// <summary>
    /// Serializer fabricated by the serialization provider for types resolved through the
    /// serializer registry: delegates every operation to the serializer mapped by the map
    /// registry, whatever kind of map serves the type. A lookup can run while maps are
    /// still registering (e.g. the driver id generator convention resolving the id member
    /// serializer of an entity model at automap), so the mapped serializer resolves lazily
    /// at each operation.
    /// </summary>
    public class MappedSerializerAdapter<TModel>(IDbContextEngine dbContextEngine) :
        SerializerBase<TModel>,
        IBsonDocumentSerializer,
        IBsonIdProvider,
        IModelMapsHandlingSerializer
    {
        // Properties.
        public IEnumerable<IModelMap> HandledModelMaps =>
            (MappedSerializer as IModelMapsHandlingSerializer)?.HandledModelMaps ?? (IEnumerable<IModelMap>)[];

        private IBsonSerializer MappedSerializer => dbContextEngine.MapRegistry.GetMappedSerializer(typeof(TModel));

        // Methods.
        public override TModel Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args) =>
            (TModel)MappedSerializer.Deserialize(context, args);

        public bool GetDocumentId(object document, out object id, out Type idNominalType, out IIdGenerator idGenerator)
        {
            if (MappedSerializer is IBsonIdProvider idProviderSerializer)
                return idProviderSerializer.GetDocumentId(document, out id, out idNominalType, out idGenerator);

            id = null!;
            idNominalType = null!;
            idGenerator = null!;
            return false;
        }

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, TModel value) =>
            MappedSerializer.Serialize(context, args, value);

        public void SetDocumentId(object document, object id)
        {
            if (MappedSerializer is IBsonIdProvider idProviderSerializer)
                idProviderSerializer.SetDocumentId(document, id);
        }

        public bool TryGetMemberSerializationInfo(string memberName, out BsonSerializationInfo serializationInfo)
        {
            if (MappedSerializer is IBsonDocumentSerializer documentSerializer)
                return documentSerializer.TryGetMemberSerializationInfo(memberName, out serializationInfo);

            serializationInfo = null!;
            return false;
        }
    }
}
