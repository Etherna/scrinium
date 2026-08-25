// Copyright 2020-present Etherna SA
// This file is part of Scrinium.
//
// Scrinium is free software: you can redistribute it and/or modify it under the terms of the
// GNU Lesser General Public License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
//
// Scrinium is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY;
// without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License along with Scrinium.
// If not, see <https://www.gnu.org/licenses/>.

using Etherna.MongoDB.Bson;
using Etherna.MongoDB.Bson.Serialization;
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.IntegrationTests.Fixtures;
using Xunit;

namespace Etherna.Scrinium.IntegrationTests
{
    /* Scrinium configures the driver with process wide state: a discriminator convention on
     * typeof(object), a serialization context accessor, and a convention pack on the global
     * convention registry. Every other consumer of the driver fork living in the same process
     * keeps working with the driver defaults: these tests run outside any db context scope,
     * on types that no db context maps. */
    [Collection("Integration")]
    public class DriverStaticStateTests(IntegrationFixture fixture)
    {
        // Internal classes.
        private enum ForeignStatus
        {
            Idle,
            Busy
        }

        private sealed class ForeignModel
        {
            public ObjectId Id { get; set; }
            public ForeignStatus Status { get; set; }
        }

        // Tests.
        [Fact]
        public void ForeignTypeDeserializesOutsideDbContextScope()
        {
            /* The discriminator convention registered on typeof(object) is inherited by every
             * type of the process: without a db context engine on the flow it has to resolve
             * types with the driver convention, instead of failing. */

            // Setup.
            var document = new BsonDocument
            {
                ["_id"] = ObjectId.GenerateNewId(),
                ["Status"] = 1
            };

            // Action.
            var model = BsonSerializer.Deserialize<ForeignModel>(document);

            // Assert.
            Assert.Equal(ForeignStatus.Busy, model.Status);
        }

        [Fact]
        public void ForeignTypeKeepsDriverEnumRepresentation()
        {
            /* The enum convention pack applies only to the class maps built while a db context
             * engine registers its maps: a foreign type automapped by the driver keeps the
             * driver default representation. */

            // Setup.
            var model = new ForeignModel
            {
                Id = ObjectId.GenerateNewId(),
                Status = ForeignStatus.Busy
            };

            // Action.
            var document = model.ToBsonDocument();

            // Assert.
            Assert.Equal(BsonType.Int32, document["Status"].BsonType);
        }

        [Fact]
        public void MappedModelKeepsStringEnumRepresentation()
        {
            // Action.
            var modelMap = fixture.TestDbContext.Engine.MapRegistry.GetModelMap(typeof(DbMigrationOperation));
            var memberMap = modelMap.ActiveSchema.TryGetMemberMap(nameof(DbMigrationOperation.CurrentStatus));

            // Assert.
            Assert.NotNull(memberMap);
            var serializer = Assert.IsAssignableFrom<IRepresentationConfigurable>(memberMap.GetSerializer());
            Assert.Equal(BsonType.String, serializer.Representation);
        }
    }
}
