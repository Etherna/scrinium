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
using Etherna.MongoDB.Driver;
using Etherna.MongoDB.Driver.Linq;
using Etherna.MongODM.Core.ExecContext.AsyncLocal;
using Etherna.MongODM.IntegrationTests.Fixtures;
using Etherna.MongODM.IntegrationTests.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.MongODM.IntegrationTests
{
    /* MODM-176: an entity id of a type serialized with a custom serializer map, with the
     * same type also serialized as plain member by other models. The db context boot
     * itself is part of the regression: registering the custom serializer must tolerate
     * the serializer fabricated for the id type while maps were still registering. */
    [Collection("Integration")]
    public class CustomIdTypeTests : IDisposable
    {
        // Fields.
        private readonly ICustomIdDbContext dbContext;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public CustomIdTypeTests(IntegrationFixture fixture)
        {
            ArgumentNullException.ThrowIfNull(fixture);
            serviceScope = fixture.ServiceProvider.CreateScope();
            dbContext = serviceScope.ServiceProvider.GetRequiredService<ICustomIdDbContext>();
        }

        public void Dispose()
        {
            serviceScope.Dispose();
            GC.SuppressFinalize(this);
        }

        // Tests.
        [Fact]
        public async Task BuildIndexesOverCustomSerializedMembers()
        {
            /* Index keys render through the document serializer and the serializer registry:
             * members of a custom serialized type, the entity id included, must support the
             * index kinds and the name rendered from the keys. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            // Action.
            await dbContext.Artifacts.BuildNewIndexesAsync();
            await dbContext.Seals.BuildNewIndexesAsync();

            // Assert.
            var artifactIndexes = await dbContext.Artifacts.AccessToCollectionAsync(async collection =>
                await (await collection.Indexes.ListAsync()).ToListAsync());
            var sealIndexes = await dbContext.Seals.AccessToCollectionAsync(async collection =>
                await (await collection.Indexes.ListAsync()).ToListAsync());

            //compound with the custom typed id
            var compoundIndex = artifactIndexes.Single(i => i["name"] == "label_by_fingerprint");
            Assert.Equal(["Label", "_id"], compoundIndex["key"].AsBsonDocument.Names);
            Assert.Equal(1, compoundIndex["key"]["Label"].ToInt32());
            Assert.Equal(-1, compoundIndex["key"]["_id"].ToInt32());

            //text
            var textIndex = artifactIndexes.Single(i => i["name"] == "label_text");
            Assert.Equal("text", textIndex["key"]["_fts"].AsString);

            //wildcard
            var wildcardIndex = artifactIndexes.Single(i => i["name"] == "wildcard_all");
            Assert.Equal(1, wildcardIndex["key"]["$**"].ToInt32());

            //unique ascending over the custom serialized member, with the name rendered from the keys
            var uniqueIndex = sealIndexes.Single(i => i["name"] == "doc_ArtifactFingerprint");
            Assert.Equal(1, uniqueIndex["key"][nameof(Seal.ArtifactFingerprint)].ToInt32());
            Assert.True(uniqueIndex["unique"].AsBoolean);

            //hashed over the custom serialized member
            var hashedIndex = sealIndexes.Single(i => i["name"] == "fingerprint_hashed");
            Assert.Equal("hashed", hashedIndex["key"][nameof(Seal.ArtifactFingerprint)].AsString);
        }

        [Fact]
        public async Task CreateAndFindEntityByCustomSerializedId()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var artifact = new Artifact(new Fingerprint("a1b2c3"), "sources");
            await dbContext.Artifacts.CreateAsync(artifact);

            // Action.
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var foundArtifact = await dbContext.Artifacts.FindOneAsync(new Fingerprint("a1b2c3"));

            // Assert.
            Assert.Equal(new Fingerprint("a1b2c3"), foundArtifact.Id);
            Assert.Equal("sources", foundArtifact.Label);
        }

        [Fact]
        public async Task CreateAndFindEntityByGeneratedGuidId()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var ticket = new Ticket("premiere");
            await dbContext.Tickets.CreateAsync(ticket);

            //the driver id generator assigned the id at insert
            Assert.NotEqual(Guid.Empty, ticket.Id);

            // Action.
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var foundTicket = await dbContext.Tickets.FindOneAsync(ticket.Id);

            // Assert.
            Assert.Equal(ticket.Id, foundTicket.Id);
            Assert.Equal("premiere", foundTicket.EventName);

            //raw document id representation
            var ticketsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("tickets");
            var rawTicket = await (await ticketsCollection.FindAsync(
                Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(ticket.Id, GuidRepresentation.Standard)))).SingleAsync();
            Assert.Equal(BsonBinarySubType.UuidStandard, rawTicket["_id"].AsBsonBinaryData.SubType);
        }

        [Fact]
        public async Task CreateAndFindEntityByGeneratedObjectIdId()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var voucher = new Voucher("free-drink");
            await dbContext.Vouchers.CreateAsync(voucher);

            //the driver id generator assigned the id at insert
            Assert.NotEqual(ObjectId.Empty, voucher.Id);

            // Action.
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var foundVoucher = await dbContext.Vouchers.FindOneAsync(voucher.Id);

            // Assert.
            Assert.Equal(voucher.Id, foundVoucher.Id);
            Assert.Equal("free-drink", foundVoucher.Code);

            //raw document id representation
            var vouchersCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("vouchers");
            var rawVoucher = await (await vouchersCollection.FindAsync(
                Builders<BsonDocument>.Filter.Eq("_id", voucher.Id))).SingleAsync();
            Assert.Equal(BsonType.ObjectId, rawVoucher["_id"].BsonType);
        }

        [Fact]
        public async Task CreateAndFindEntityByIntId()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var badge = new Badge(42, "founder");
            await dbContext.Badges.CreateAsync(badge);

            // Action.
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var foundBadge = await dbContext.Badges.FindOneAsync(42);

            // Assert.
            Assert.Equal(42, foundBadge.Id);
            Assert.Equal("founder", foundBadge.Title);

            //raw document id representation
            var badgesCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("badges");
            var rawBadge = await (await badgesCollection.FindAsync(
                Builders<BsonDocument>.Filter.Eq("_id", 42))).SingleAsync();
            Assert.Equal(BsonType.Int32, rawBadge["_id"].BsonType);
        }

        [Fact]
        public async Task CustomSerializedIdPersistsWithItsCustomRepresentation()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var artifact = new Artifact(new Fingerprint("f0e1d2"), "binaries");
            await dbContext.Artifacts.CreateAsync(artifact);

            // Action.
            var artifactsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("artifacts");
            var rawArtifact = await (await artifactsCollection.FindAsync(
                Builders<BsonDocument>.Filter.Eq("_id", "f0e1d2"))).SingleAsync();

            // Assert.
            Assert.Equal(BsonType.String, rawArtifact["_id"].BsonType);
            Assert.Equal("binaries", rawArtifact["Label"].AsString);
        }

        [Fact]
        public async Task CustomSerializedMemberPersistsWithItsCustomRepresentation()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var seal = new Seal(new Fingerprint("09f8e7"));
            await dbContext.Seals.CreateAsync(seal);

            // Action.
            var sealsCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("seals");
            var rawSeal = await (await sealsCollection.FindAsync(
                Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(seal.Id)))).SingleAsync();

            // Assert.
            Assert.Equal("09f8e7", rawSeal[nameof(Seal.ArtifactFingerprint)].AsString);
        }

        [Fact]
        public async Task QueryEntityByCustomSerializedMember()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var seal = new Seal(new Fingerprint("456def"));
            await dbContext.Seals.CreateAsync(seal);

            // Action.
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var foundSeal = await dbContext.Seals.QueryElementsAsync(elements =>
                elements.Where(s => s.ArtifactFingerprint == new Fingerprint("456def"))
                        .SingleAsync());

            // Assert.
            Assert.Equal(seal.Id, foundSeal.Id);
            Assert.Equal(new Fingerprint("456def"), foundSeal.ArtifactFingerprint);
        }

        [Fact]
        public async Task SerializeIdsWithTheirExpectedDocumentRepresentation()
        {
            /* Pins the effective serialized shape of every supported id type, comparing
             * the whole raw documents with their human readable shell json: custom
             * serialized value type and int persist as given, string persists as
             * ObjectId, Guid as standard UUID binary, ObjectId as native ObjectId. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            var artifact = new Artifact(new Fingerprint("1a2b3c"), "docs");
            var badge = new Badge(7, "early bird");
            var seal = new Seal(new Fingerprint("d4e5f6"));
            var ticket = new Ticket("premiere night");
            var voucher = new Voucher("welcome");
            await dbContext.Artifacts.CreateAsync(artifact);
            await dbContext.Badges.CreateAsync(badge);
            await dbContext.Seals.CreateAsync(seal);
            await dbContext.Tickets.CreateAsync(ticket);
            await dbContext.Vouchers.CreateAsync(voucher);

            async Task<string> RenderRawDocumentAsync(string collectionName, BsonValue id)
            {
                var collection = dbContext.Engine.Database.GetCollection<BsonDocument>(collectionName);
                var document = await (await collection.FindAsync(
                    Builders<BsonDocument>.Filter.Eq("_id", id))).SingleAsync();
                return document.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.Shell });
            }

            // Action & Assert.
            /* The server always stores the _id element first, whatever the serialized
             * element order. */
            Assert.Equal(
                """{ "_id" : "1a2b3c", "_s" : "d0d3a6b1-6c1a-4a90-89cf-8b73e9e968ad", "Label" : "docs" }""",
                await RenderRawDocumentAsync("artifacts", "1a2b3c"));
            Assert.Equal(
                """{ "_id" : 7, "_s" : "3f6a5b0c-9d2e-4b81-a4c6-8e3d7f21b95a", "Title" : "early bird" }""",
                await RenderRawDocumentAsync("badges", 7));
            Assert.Equal(
                $$"""{ "_id" : ObjectId("{{seal.Id}}"), "_s" : "b6a0fc17-3a19-44cf-a725-1b3f47b164c9", "ArtifactFingerprint" : "d4e5f6" }""",
                await RenderRawDocumentAsync("seals", ObjectId.Parse(seal.Id)));
            Assert.Equal(
                $$"""{ "_id" : UUID("{{ticket.Id}}"), "_s" : "a1d9c3e7-2f58-4b06-8c41-7d3e9f0b52c6", "EventName" : "premiere night" }""",
                await RenderRawDocumentAsync("tickets", new BsonBinaryData(ticket.Id, GuidRepresentation.Standard)));
            Assert.Equal(
                $$"""{ "_id" : ObjectId("{{voucher.Id}}"), "_s" : "c25e8f07-31db-49a8-b6c4-5d18e9f3a02b", "Code" : "welcome" }""",
                await RenderRawDocumentAsync("vouchers", voucher.Id));
        }

        [Fact]
        public async Task UniqueIndexOverCustomSerializedMemberDeniesDuplicates()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            await dbContext.Seals.BuildNewIndexesAsync();
            await dbContext.Seals.CreateAsync(new Seal(new Fingerprint("dup001")));

            // Action & Assert.
            await Assert.ThrowsAsync<MongoWriteException>(() =>
                dbContext.Seals.CreateAsync(new Seal(new Fingerprint("dup001"))));
        }
    }
}
