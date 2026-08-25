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
using Etherna.MongoDB.Bson.Serialization;
using Etherna.MongoDB.Bson.Serialization.Serializers;
using Etherna.MongoDB.Driver;
using Etherna.Scrinium.Core.ExecContext;
using Etherna.Scrinium.Core.Models;
using Etherna.Scrinium.Core.Options;
using Etherna.Scrinium.Core.Serialization.Mapping;
using Etherna.Scrinium.Core.Utility;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.Core.Repositories
{
    public class RepositoryTest
    {
        // Internal classes.
        /// <summary>
        /// A mapped custom serializer emitting the given document whatever the model: its
        /// element names play the ones a class map emits, the keys of an own extra elements
        /// bag included.
        /// </summary>
        public sealed class FakeDocumentSerializer(BsonDocument document)
            : SerializerBase<FakeModel>
        {
            public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, FakeModel value) =>
                BsonDocumentSerializer.Instance.Serialize(context, document);
        }

        // Fields.
        private readonly Mock<IMongoCollection<FakeModel>> collectionMock = new();
        private readonly Mock<IDbContext> dbContextMock = new();
        private readonly Mock<IDbContextEngine> engineMock = new();
        private readonly Dictionary<object, object?> executionContextItems = [];
        private readonly Mock<IExecutionContext> executionContextMock = new();
        private readonly Mock<IMapRegistry> mapRegistryMock = new();
        private readonly Mock<IModelMap> modelMapMock = new();
        private readonly Mock<IDbContextOptions> optionsMock = new();
        private readonly BsonSerializerRegistry serializerRegistry = new();

        // Constructor.
        public RepositoryTest()
        {
            /* Model map exposing the id member maps of two referenced documents: a single
             * reference member, and an enumerable of references. */
            var classMap = new BsonClassMap<FakeModel>(cm =>
            {
                cm.MapMember(m => m.EnumerableProp);
                cm.MapMember(m => m.ObjectProp);
            });
            classMap.Freeze();
            //the id is mapped on the level declaring it
            var idClassMap = new BsonClassMap<FakeEntityModelBase<string>>(cm => cm.MapIdMember(m => m.Id));
            idClassMap.Freeze();
            modelMapMock.Setup(m => m.AllDescendingMemberMaps)
                .Returns([
                    ReferenceIdMemberMap(classMap.GetMemberMap(m => m.ObjectProp), idClassMap.IdMemberMap!),
                    ReferenceIdMemberMap(classMap.GetMemberMap(m => m.EnumerableProp), idClassMap.IdMemberMap!)
                ]);
            var modelMap = modelMapMock.Object;
            mapRegistryMock.Setup(r => r.TryGetModelMap(It.IsAny<Type>(), out modelMap))
                .Returns(true);

            /* Index keys render against the collection serializers: a document serializer
             * that can't resolve the members renders the field paths verbatim. */
            collectionMock.Setup(c => c.DocumentSerializer)
                .Returns(new Mock<IBsonSerializer<FakeModel>>().Object);
            collectionMock.Setup(c => c.Settings)
                .Returns(new MongoCollectionSettings { SerializerRegistry = new BsonSerializerRegistry() });

            //update definitions render against the engine registry
            serializerRegistry.RegisterSerializationProvider(new BsonObjectModelSerializationProvider());
            serializerRegistry.RegisterSerializationProvider(new PrimitiveSerializationProvider());

            executionContextMock.Setup(c => c.Items).Returns(executionContextItems);
            optionsMock.Setup(o => o.DbName).Returns("test-db");
            engineMock.Setup(e => e.ExecutionContext).Returns(executionContextMock.Object);
            engineMock.Setup(e => e.MapRegistry).Returns(mapRegistryMock.Object);
            engineMock.Setup(e => e.Options).Returns(optionsMock.Object);
            engineMock.Setup(e => e.SerializerRegistry).Returns(serializerRegistry);
            engineMock.Setup(e => e.GetMongoCollection<FakeModel>(It.IsAny<string>(), It.IsAny<MongoCollectionSettings>(), It.IsAny<bool>()))
                .Returns(collectionMock.Object);
            dbContextMock.Setup(c => c.Engine).Returns(engineMock.Object);
        }

        // Tests.
        [Fact]
        public async Task AccessToCollectionDisposesTheAmbientHandlerWhenTheAccessedOperationSucceeds()
        {
            // Setup.
            var repository = BuildRepository();

            // Action.
            await repository.AccessToCollectionAsync(_ => Task.FromResult(0));

            // Assert.
            Assert.Empty(GetAmbientDbExecContextHandlers());
        }

        [Fact]
        public async Task AccessToCollectionDisposesTheAmbientHandlerWhenTheAccessedOperationThrows()
        {
            /* A leaked handler would stay registered in the flow items and become the
             * ambient one again once the handlers above it complete, resolving the wrong
             * db context, repository and serializer registry for the rest of the flow. */

            // Setup.
            var repository = BuildRepository();

            // Action.
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.AccessToCollectionAsync<int>(_ => throw new InvalidOperationException()));

            // Assert.
            Assert.Empty(GetAmbientDbExecContextHandlers());
        }

        [Fact]
        public async Task DefinedIndexesBuildAnAutomaticIndexForEachReferenceIdPath()
        {
            // Setup.
            var repository = BuildRepository();

            // Action.
            var indexes = await repository.GetDefinedIndexModelsAsync();

            // Assert.
            Assert.Equal(
                ["ref_ObjectProp._id", "ref_EnumerableProp._id"],
                indexes.Select(i => i.Options.Name));
        }

        [Fact]
        public async Task DefinedIndexesKeepTheAutomaticIndexOnAReferenceIdPathIndexedWithoutASortOrder()
        {
            /* Only an ascending or descending key serves every query on its field: a hashed
             * or text key on the reference id path doesn't replace the automatic index. */

            // Setup.
            var repository = BuildRepository(
                (Builders<FakeModel>.IndexKeys.Hashed("ObjectProp._id"),
                    new CreateIndexOptions<FakeModel> { Name = "custom" }));

            // Action.
            var indexes = await repository.GetDefinedIndexModelsAsync();

            // Assert.
            Assert.Equal(
                ["custom", "ref_ObjectProp._id", "ref_EnumerableProp._id"],
                indexes.Select(i => i.Options.Name));
        }

        [Fact]
        public async Task DefinedIndexesKeepTheAutomaticIndexOnAReferenceIdPathNotOpeningACustomIndex()
        {
            /* A compound index doesn't serve the queries on a field following its first key:
             * the automatic index on the reference id path is not a duplicate of it. */

            // Setup.
            var repository = BuildRepository(
                (Builders<FakeModel>.IndexKeys.Ascending("StringProp").Ascending("ObjectProp._id"),
                    new CreateIndexOptions<FakeModel> { Name = "custom" }));

            // Action.
            var indexes = await repository.GetDefinedIndexModelsAsync();

            // Assert.
            Assert.Equal(
                ["custom", "ref_ObjectProp._id", "ref_EnumerableProp._id"],
                indexes.Select(i => i.Options.Name));
        }

        [Fact]
        public async Task DefinedIndexesSkipTheAutomaticIndexOnAReferenceIdPathIndexedByACustomIndex()
        {
            // Setup.
            var repository = BuildRepository(
                (Builders<FakeModel>.IndexKeys.Ascending("ObjectProp._id"),
                    new CreateIndexOptions<FakeModel> { Unique = true }));

            // Action.
            var indexes = await repository.GetDefinedIndexModelsAsync();

            // Assert.
            Assert.Equal(
                ["doc_ObjectProp._id", "ref_EnumerableProp._id"],
                indexes.Select(i => i.Options.Name));
        }

        [Fact]
        public async Task DefinedIndexesSkipTheAutomaticIndexOnAReferenceIdPathOpeningACompoundCustomIndex()
        {
            // Setup.
            var repository = BuildRepository(
                (Builders<FakeModel>.IndexKeys.Descending("ObjectProp._id").Ascending("StringProp"),
                    new CreateIndexOptions<FakeModel> { Name = "custom" }));

            // Action.
            var indexes = await repository.GetDefinedIndexModelsAsync();

            // Assert.
            Assert.Equal(
                ["custom", "ref_EnumerableProp._id"],
                indexes.Select(i => i.Options.Name));
        }

        [Fact]
        public async Task FindDisposesTheAmbientHandlerWhenTheCollectionAccessThrows()
        {
            // Setup.
            collectionMock.Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<FakeModel>>(),
                    It.IsAny<FindOptions<FakeModel, FakeModel>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException());
            var repository = BuildRepository();

            // Action.
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.FindAsync<FakeModel>(Builders<FakeModel>.Filter.Empty));

            // Assert.
            Assert.Empty(GetAmbientDbExecContextHandlers());
        }

        [Fact]
        public async Task FindKeepsTheAmbientHandlerUntilTheCursorIsDisposed()
        {
            // Setup.
            collectionMock.Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<FakeModel>>(),
                    It.IsAny<FindOptions<FakeModel, FakeModel>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Mock<IAsyncCursor<FakeModel>>().Object);
            var repository = BuildRepository();

            // Action.
            var cursor = await repository.FindAsync<FakeModel>(Builders<FakeModel>.Filter.Empty);

            // Assert.
            //the handler serves the cursor consumption, and releases with its disposal
            Assert.Single(GetAmbientDbExecContextHandlers());
            cursor.Dispose();
            Assert.Empty(GetAmbientDbExecContextHandlers());
        }

        [Fact]
        public async Task TryFindOneDisposesTheAmbientHandlerOnAMissingId()
        {
            /* A miss is ordinary control flow: the collection access throws
             * MongodmEntityNotFoundException, and TryFindOneAsync turns it into a null
             * result. The ambient handler must release also on this path. */

            // Setup.
            var cursorMock = new Mock<IAsyncCursor<FakeModel>>();
            cursorMock.Setup(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            collectionMock.Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<FakeModel>>(),
                    It.IsAny<FindOptions<FakeModel, FakeModel>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);
            var repository = BuildRepository();

            // Action.
            var result = await repository.TryFindOneAsync("missingId");

            // Assert.
            Assert.Null(result);
            Assert.Empty(GetAmbientDbExecContextHandlers());
        }

        [Fact]
        public async Task UpsertComposesTheOnInsertFieldsFromTheSerializedModelElements()
        {
            // Setup.
            var modelSerializer = new FakeDocumentSerializer(
                new BsonDocument { { "_id", 42 }, { "IntegerProp", 42 }, { "StringProp", 42 } });
            mapRegistryMock.Setup(r => r.GetMappedSerializer(typeof(FakeModel)))
                .Returns(modelSerializer);
            var upsertUpdates = CaptureUpsertUpdates();
            var repository = BuildRepository();

            // Action.
            await repository.UpsertIncrementAsync(
                Builders<FakeModel>.Filter.Empty,
                new StringFieldDefinition<FakeModel, int>("IntegerProp"),
                1,
                new FakeModel());

            // Assert.
            //the id and the incremented field stay out of the on insert instructions
            Assert.Equal(
                """{ "$setOnInsert" : { "StringProp" : 42 }, "$inc" : { "IntegerProp" : 1 } }""",
                upsertUpdates.Single()
                    .Render(new(modelSerializer, serializerRegistry))
                    .ToJson());
        }

        [Fact]
        public async Task UpsertKeepsANestedOnInsertFieldNameNavigatingNestedDocumentsInsideAWholeElement()
        {
            /* A sub document set whole carries its element names as values, like an insert
             * writes them: only the names composing an update field name are constrained. */

            // Setup.
            var modelSerializer = new FakeDocumentSerializer(new BsonDocument
            {
                { "_id", 42 },
                { "IntegerProp", 42 },
                { "Counters", new BsonDocument { { "likes.total", 5 } } }
            });
            mapRegistryMock.Setup(r => r.GetMappedSerializer(typeof(FakeModel)))
                .Returns(modelSerializer);
            var upsertUpdates = CaptureUpsertUpdates();
            var repository = BuildRepository();

            // Action.
            await repository.UpsertIncrementAsync(
                Builders<FakeModel>.Filter.Empty,
                new StringFieldDefinition<FakeModel, int>("IntegerProp"),
                1,
                new FakeModel());

            // Assert.
            Assert.Equal(
                """{ "$setOnInsert" : { "Counters" : { "likes.total" : 5 } }, "$inc" : { "IntegerProp" : 1 } }""",
                upsertUpdates.Single()
                    .Render(new(modelSerializer, serializerRegistry))
                    .ToJson());
        }

        [Fact]
        public async Task UpsertKeepsAnOnInsertFieldNameStartingWithTheOperatorPrefix()
        {
            /* A '$' prefixed name inside a $setOnInsert isn't read as an operator: the server
             * stores the field with that name, like an insert of the same model would. */

            // Setup.
            var modelSerializer = new FakeDocumentSerializer(
                new BsonDocument { { "_id", 42 }, { "IntegerProp", 42 }, { "$where", 42 } });
            mapRegistryMock.Setup(r => r.GetMappedSerializer(typeof(FakeModel)))
                .Returns(modelSerializer);
            var upsertUpdates = CaptureUpsertUpdates();
            var repository = BuildRepository();

            // Action.
            await repository.UpsertIncrementAsync(
                Builders<FakeModel>.Filter.Empty,
                new StringFieldDefinition<FakeModel, int>("IntegerProp"),
                1,
                new FakeModel());

            // Assert.
            Assert.Equal(
                """{ "$setOnInsert" : { "$where" : 42 }, "$inc" : { "IntegerProp" : 1 } }""",
                upsertUpdates.Single()
                    .Render(new(modelSerializer, serializerRegistry))
                    .ToJson());
        }

        [Fact]
        public async Task UpsertKeepsOutTheWholeOnInsertElementAnUpdatedFieldWrites()
        {
            /* An updated field writing a whole sub document leaves no sub element to set on
             * insert: any of them would conflict with the update. */

            // Setup.
            var modelSerializer = new FakeDocumentSerializer(new BsonDocument
            {
                { "_id", 42 },
                { "Subject", "song" },
                { "Counters", new BsonDocument { { "plays", 3 }, { "likes", 5 } } }
            });
            mapRegistryMock.Setup(r => r.GetMappedSerializer(typeof(FakeModel)))
                .Returns(modelSerializer);
            var upsertUpdates = CaptureUpsertUpdates();
            var repository = BuildRepository();

            // Action.
            await repository.UpsertSetFieldAsync(
                Builders<FakeModel>.Filter.Empty,
                new StringFieldDefinition<FakeModel, BsonDocument>("Counters"),
                new BsonDocument { { "plays", 0 } },
                new FakeModel());

            // Assert.
            Assert.Equal(
                """{ "$setOnInsert" : { "Subject" : "song" }, "$set" : { "Counters" : { "plays" : 0 } } }""",
                upsertUpdates.Single()
                    .Render(new(modelSerializer, serializerRegistry))
                    .ToJson());
        }

        [Fact]
        public async Task UpsertRejectsAnEmptyOnInsertFieldName()
        {
            /* An empty name has no update path addressing it: the server refuses the whole
             * update, the one on an existing document included. */

            // Setup.
            mapRegistryMock.Setup(r => r.GetMappedSerializer(typeof(FakeModel)))
                .Returns(new FakeDocumentSerializer(new BsonDocument
                {
                    { "_id", 42 },
                    { "Counters", new BsonDocument { { "plays", 3 }, { "", 5 } } }
                }));
            var upsertUpdates = CaptureUpsertUpdates();
            var repository = BuildRepository();

            // Action.
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.UpsertIncrementAsync(
                    Builders<FakeModel>.Filter.Empty,
                    new StringFieldDefinition<FakeModel, int>("Counters.plays"),
                    1,
                    new FakeModel()));

            // Assert.
            Assert.Contains("fakeModels", exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(FakeModel), exception.Message, StringComparison.Ordinal);
            Assert.Contains("empty name", exception.Message, StringComparison.Ordinal);
            Assert.Contains("\"Counters\"", exception.Message, StringComparison.Ordinal);
            Assert.Empty(upsertUpdates);
        }

        [Fact]
        public async Task UpsertRejectsANestedOnInsertFieldNameNavigatingNestedDocuments()
        {
            /* The element names of a split sub document compose update field names like the
             * top level ones do, and a dotted one is ambiguous the same way. */

            // Setup.
            mapRegistryMock.Setup(r => r.GetMappedSerializer(typeof(FakeModel)))
                .Returns(new FakeDocumentSerializer(new BsonDocument
                {
                    { "_id", 42 },
                    { "Counters", new BsonDocument { { "plays", 3 }, { "likes.total", 5 } } }
                }));
            var upsertUpdates = CaptureUpsertUpdates();
            var repository = BuildRepository();

            // Action.
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.UpsertIncrementAsync(
                    Builders<FakeModel>.Filter.Empty,
                    new StringFieldDefinition<FakeModel, int>("Counters.plays"),
                    1,
                    new FakeModel()));

            // Assert.
            Assert.Contains("fakeModels", exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(FakeModel), exception.Message, StringComparison.Ordinal);
            Assert.Contains("\"likes.total\"", exception.Message, StringComparison.Ordinal);
            Assert.Contains("\"Counters\"", exception.Message, StringComparison.Ordinal);
            Assert.Empty(upsertUpdates);
        }

        [Fact]
        public async Task UpsertRejectsAnOnInsertFieldNameNavigatingNestedDocuments()
        {
            /* The on insert field names come from the serialized model elements, and the
             * driver doesn't validate them: a dotted name silently writes a nested field
             * instead of the literal one. */

            // Setup.
            mapRegistryMock.Setup(r => r.GetMappedSerializer(typeof(FakeModel)))
                .Returns(new FakeDocumentSerializer(
                    new BsonDocument { { "_id", 42 }, { "IntegerProp", 42 }, { "nested.field", 42 } }));
            var upsertUpdates = CaptureUpsertUpdates();
            var repository = BuildRepository();

            // Action.
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.UpsertIncrementAsync(
                    Builders<FakeModel>.Filter.Empty,
                    new StringFieldDefinition<FakeModel, int>("IntegerProp"),
                    1,
                    new FakeModel()));

            // Assert.
            Assert.Contains("fakeModels", exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(FakeModel), exception.Message, StringComparison.Ordinal);
            Assert.Contains("nested.field", exception.Message, StringComparison.Ordinal);
            Assert.Empty(upsertUpdates);
        }

        [Fact]
        public async Task UpsertRejectsAnOnInsertValueAnUpdatedFieldNavigatesInto()
        {
            /* A path inside an array isn't an independent branch the value can split on, and
             * the array can't stay whole beside it: without the model value, the update alone
             * would insert a document where the model maps an array. */

            // Setup.
            mapRegistryMock.Setup(r => r.GetMappedSerializer(typeof(FakeModel)))
                .Returns(new FakeDocumentSerializer(new BsonDocument
                {
                    { "_id", 42 },
                    { "Subject", "song" },
                    { "Tags", new BsonArray { "rock", "jazz" } }
                }));
            var upsertUpdates = CaptureUpsertUpdates();
            var repository = BuildRepository();

            // Action.
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.UpsertSetFieldAsync(
                    Builders<FakeModel>.Filter.Empty,
                    new StringFieldDefinition<FakeModel, string>("Tags.0"),
                    "pop",
                    new FakeModel()));

            // Assert.
            Assert.Contains("fakeModels", exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(FakeModel), exception.Message, StringComparison.Ordinal);
            Assert.Contains("\"Tags\"", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Array", exception.Message, StringComparison.Ordinal);
            Assert.Empty(upsertUpdates);
        }

        [Fact]
        public async Task UpsertSplitsTheOnInsertElementsTheUpdatedFieldsNavigateInto()
        {
            /* The on insert instructions can't set whole an element the update writes inside,
             * or the server refuses the update as a conflict: the element splits into its sub
             * elements, following every updated field navigating into it, and the updated
             * fields stay out alone. */

            // Setup.
            var modelSerializer = new FakeDocumentSerializer(new BsonDocument
            {
                { "_id", 42 },
                { "Subject", "song" },
                {
                    "Counters", new BsonDocument
                    {
                        { "plays", 3 },
                        { "daily", new BsonDocument { { "mon", 1 }, { "tue", 2 } } }
                    }
                }
            });
            mapRegistryMock.Setup(r => r.GetMappedSerializer(typeof(FakeModel)))
                .Returns(modelSerializer);
            var upsertUpdates = CaptureUpsertUpdates();
            var repository = BuildRepository();

            // Action.
            await repository.UpsertAsync(
                Builders<FakeModel>.Filter.Empty,
                Builders<FakeModel>.Update.Combine(
                    Builders<FakeModel>.Update.Inc<int>("Counters.plays", 1),
                    Builders<FakeModel>.Update.Inc<int>("Counters.daily.mon", 1)),
                new FakeModel(),
                [
                    new StringFieldDefinition<FakeModel, int>("Counters.plays"),
                    new StringFieldDefinition<FakeModel, int>("Counters.daily.mon")
                ]);

            // Assert.
            Assert.Equal(
                """{ "$setOnInsert" : { "Subject" : "song", "Counters.daily.tue" : 2 }, "$inc" : { "Counters.plays" : 1, "Counters.daily.mon" : 1 } }""",
                upsertUpdates.Single()
                    .Render(new(modelSerializer, serializerRegistry))
                    .ToJson());
        }

        // Helpers.
        private Repository<FakeModel, string> BuildRepository(
            params (IndexKeysDefinition<FakeModel> keys, CreateIndexOptions<FakeModel> options)[] indexBuilders)
        {
            var repository = new Repository<FakeModel, string>(
                new RepositoryOptions<FakeModel>("fakeModels") { IndexBuilders = indexBuilders });
            repository.Initialize(dbContextMock.Object, new Mock<ILogger>().Object);
            return repository;
        }

        private List<UpdateDefinition<FakeModel>> CaptureUpsertUpdates()
        {
            List<UpdateDefinition<FakeModel>> updates = [];
            collectionMock.Setup(c => c.FindOneAndUpdateAsync(
                    It.IsAny<FilterDefinition<FakeModel>>(),
                    It.IsAny<UpdateDefinition<FakeModel>>(),
                    It.IsAny<FindOneAndUpdateOptions<FakeModel, FakeModel>>(),
                    It.IsAny<CancellationToken>()))
                .Callback((
                    FilterDefinition<FakeModel> _,
                    UpdateDefinition<FakeModel> update,
                    FindOneAndUpdateOptions<FakeModel, FakeModel> _,
                    CancellationToken _) => updates.Add(update))
                .ReturnsAsync((FakeModel)null!);
            return updates;
        }

        private IEnumerable<DbExecutionContextHandler> GetAmbientDbExecContextHandlers() =>
            executionContextItems.Values
                .OfType<IEnumerable<DbExecutionContextHandler>>()
                .SelectMany(handlers => handlers);

        private static IMemberMap ReferenceIdMemberMap(params BsonMemberMap[] elementPath)
        {
            var memberMapPath = elementPath.Select(bsonMemberMap =>
            {
                var pathMemberMapMock = new Mock<IMemberMap>();
                pathMemberMapMock.Setup(mm => mm.BsonMemberMap).Returns(bsonMemberMap);
                return pathMemberMapMock.Object;
            }).ToArray();

            var memberMapMock = new Mock<IMemberMap>();
            memberMapMock.Setup(mm => mm.IsEntityReferenceMember).Returns(true);
            memberMapMock.Setup(mm => mm.IsIdMember).Returns(true);
            memberMapMock.Setup(mm => mm.MemberMapPath).Returns(memberMapPath);
            return memberMapMock.Object;
        }
    }
}
