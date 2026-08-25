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
using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Etherna.Scrinium.Core.Options;
using Etherna.Scrinium.IntegrationTests.Fixtures;
using Etherna.Scrinium.IntegrationTests.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.IntegrationTests
{
    /* MODM-231: typeof(object) never gets a model map, so the driver ObjectSerializer
     * stays the registered serializer for object. Its allowed types guard denies mapped
     * model types selected by document discriminators into object shaped members, and
     * its presence unblocks interface typed members, whose driver serializer requires it. */
    [Collection("Integration")]
    public class ObjectMemberSerializationTests : IDisposable
    {
        // Fields.
        private readonly IObjectMembersDbContext dbContext;
        private readonly IntegrationFixture fixture;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public ObjectMemberSerializationTests(IntegrationFixture fixture)
        {
            ArgumentNullException.ThrowIfNull(fixture);
            this.fixture = fixture;
            serviceScope = fixture.ServiceProvider.CreateScope();
            dbContext = serviceScope.ServiceProvider.GetRequiredService<IObjectMembersDbContext>();
        }

        public void Dispose()
        {
            serviceScope.Dispose();
            GC.SuppressFinalize(this);
        }

        // Tests.
        [Fact]
        public void EngineBuildsWithInterfaceTypedMember()
        {
            /* The driver discriminated interface serializer installs itself only over the
             * driver ObjectSerializer: with it registered for object, a model with an
             * interface typed member initializes its member maps at engine build. */

            // Setup.
            var interfaceMemberDbContext = new InterfaceMemberDbContext();
            var dependencies = fixture.ServiceProvider.GetRequiredService<IDbDependencies>();
            var options = new DbContextOptions
            {
                ConnectionString = $"{fixture.MongoDbUrl}/scrinium-it-interface-member"
            };

            // Action.
            var engine = interfaceMemberDbContext.BuildEngine(dependencies, new MongoClient(fixture.MongoDbUrl), options);

            // Assert.
            var noticeMap = engine.MapRegistry.GetModelMap(typeof(Notice));
            Assert.Contains(
                noticeMap.DefinedMemberMaps,
                mm => mm.BsonMemberMap.MemberInfo.Name == nameof(Notice.Attachment));
        }

        [Fact]
        public async Task ObjectMemberDeserializationDeniesMappedModelType()
        {
            /* A document can shape a sub-document of an object member with the
             * discriminator of any model type mapped in the db context: the driver object
             * serializer denies the type, instead of instantiating the model with document
             * supplied members. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var capsuleId = await InsertRawCapsuleDocumentAsync(new BsonDocument
            {
                ["Payload"] = new BsonDocument
                {
                    ["_t"] = SecretDiscriminator,
                    ["Value"] = "forged"
                }
            });
            var constructedInstancesBefore = Secret.TotalConstructedInstances;

            // Action.
            var exception = await Record.ExceptionAsync(
                () => dbContext.Capsules.FindOneAsync(capsuleId));

            // Assert.
            Assert.NotNull(exception);
            var guardException = TryFindInnerException<BsonSerializationException>(exception);
            Assert.NotNull(guardException);
            Assert.Contains("not configured as a type that is allowed to be deserialized", guardException.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(Secret), guardException.Message, StringComparison.Ordinal);
            Assert.Equal(constructedInstancesBefore, Secret.TotalConstructedInstances);
        }

        [Fact]
        public async Task ObjectMemberSerializationDeniesMappedModelType()
        {
            /* Writing a mapped model into an object shaped member fails loudly at save:
             * hosting application types into object shaped members needs an
             * ObjectSerializer with an explicit allow list, registered for object through
             * a custom serializer map. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var capsule = new Capsule(new Secret("hidden"));

            // Action.
            var exception = await Record.ExceptionAsync(
                () => dbContext.Capsules.CreateAsync(capsule));

            // Assert.
            Assert.NotNull(exception);
            var guardException = TryFindInnerException<BsonSerializationException>(exception);
            Assert.NotNull(guardException);
            Assert.Contains("not configured as a type that is allowed to be serialized", guardException.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(Secret), guardException.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ObjectMembersRoundTripFrameworkValues()
        {
            /* Values of the driver default allowed framework types keep round tripping
             * through object shaped members. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var capsule = new Capsule("plain payload")
            {
                Metadata = new Dictionary<string, object>
                {
                    ["count"] = 42,
                    ["label"] = "tagged"
                }
            };
            await dbContext.Capsules.CreateAsync(capsule);

            // Action.
            //read from a fresh scope, deserializing from db instead of the identity map
            using var readScope = fixture.ServiceProvider.CreateScope();
            var readDbContext = readScope.ServiceProvider.GetRequiredService<IObjectMembersDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var foundCapsule = await readDbContext.Capsules.FindOneAsync(capsule.Id);

            // Assert.
            Assert.Equal("plain payload", foundCapsule.Payload);
            Assert.NotNull(foundCapsule.Metadata);
            Assert.Equal(42, (int)foundCapsule.Metadata["count"]);
            Assert.Equal("tagged", (string)foundCapsule.Metadata["label"]);
        }

        [Fact]
        public async Task ObjectMetadataBagValueDeserializationDeniesMappedModelType()
        {
            /* Values of a metadata bag member resolve the object serializer too: the
             * allowed types guard denies mapped model types also there. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var capsuleId = await InsertRawCapsuleDocumentAsync(new BsonDocument
            {
                ["Metadata"] = new BsonDocument
                {
                    ["note"] = new BsonDocument
                    {
                        ["_t"] = SecretDiscriminator,
                        ["Value"] = "forged"
                    }
                }
            });
            var constructedInstancesBefore = Secret.TotalConstructedInstances;

            // Action.
            var exception = await Record.ExceptionAsync(
                () => dbContext.Capsules.FindOneAsync(capsuleId));

            // Assert.
            Assert.NotNull(exception);
            var guardException = TryFindInnerException<BsonSerializationException>(exception);
            Assert.NotNull(guardException);
            Assert.Contains("not configured as a type that is allowed to be deserialized", guardException.Message, StringComparison.Ordinal);
            Assert.Equal(constructedInstancesBefore, Secret.TotalConstructedInstances);
        }

        // Helpers.
        /* The discriminator of the active Secret schema, selectable on documents by who
         * writes raw content into any object shaped element. */
        private string SecretDiscriminator =>
            dbContext.Engine.MapRegistry.GetModelMap(typeof(Secret)).ActiveSchema.Discriminator;

        /// <summary>
        /// Persist a raw capsule document carrying the given elements, out of the
        /// serialization pipeline, and return its id.
        /// </summary>
        private async Task<string> InsertRawCapsuleDocumentAsync(BsonDocument elements)
        {
            var capsuleId = ObjectId.GenerateNewId();
            var capsuleDocument = new BsonDocument
            {
                ["_id"] = capsuleId,
                ["_s"] = ObjectMembersDbContext.CapsuleSchemaId
            };
            capsuleDocument.AddRange(elements);

            var capsulesCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("capsules");
            await capsulesCollection.InsertOneAsync(capsuleDocument);

            return capsuleId.ToString();
        }

        private static TException? TryFindInnerException<TException>(Exception? exception)
            where TException : Exception
        {
            for (var current = exception; current is not null; current = current.InnerException)
                if (current is TException found)
                    return found;
            return null;
        }
    }
}
