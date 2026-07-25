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
using Etherna.MongoDB.Bson.IO;
using Etherna.MongoDB.Bson.Serialization;
using Etherna.MongoDB.Bson.Serialization.Serializers;
using Xunit;

namespace Etherna.MongODM.Core
{
    public class DriverSerializationBehaviorTest
    {
        // Internal classes.
        private class NominalModel
        { }

        private sealed class ActualModel : NominalModel
        { }

        private sealed class ActualModelSerializer : SerializerBase<ActualModel>
        {
            // Properties.
            public bool WasInvoked { get; private set; }

            // Methods.
            public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, ActualModel value)
            {
                WasInvoked = true;
                context.Writer.WriteStartDocument();
                context.Writer.WriteEndDocument();
            }
        }

        // Tests.
        [Fact]
        public void BsonClassMapSerializerHonorsSerializeAsNominalType()
        {
            /* Proxy model serialization relies on the driver honoring SerializeAsNominalType in
             * class map serialization: a proxy instance serializes through the class map of its
             * purged type, and a driver ignoring the flag would delegate to the serializer of
             * the actual (proxy) type instead, recursing infinitely. The behavior is provided
             * by the Etherna.MongoDB.Driver fork since 3.10.1, and proposed upstream with
             * CSHARP-3153: this test pins the contract, and must stay green on every driver
             * bump. */

            // Setup.
            //a sentinel serializer reveals the delegation to the actual type
            var sentinel = new ActualModelSerializer();
            BsonSerializer.RegisterSerializer(sentinel);

            var nominalClassMap = new BsonClassMap<NominalModel>(cm => cm.AutoMap());
            nominalClassMap.Freeze();
            var nominalSerializer = new BsonClassMapSerializer<NominalModel>(nominalClassMap);

            using var bsonWriter = new BsonDocumentWriter(new BsonDocument());
            var context = BsonSerializationContext.CreateRoot(bsonWriter);
            var args = new BsonSerializationArgs
            {
                ForceStaticSerializerRegistry = true,
                NominalType = typeof(NominalModel),
                SerializeAsNominalType = true
            };

            // Action.
            nominalSerializer.Serialize(context, args, new ActualModel());

            // Assert.
            Assert.False(sentinel.WasInvoked);
        }
    }
}
