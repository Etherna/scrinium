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
using Etherna.MongODM.Core.Comparers;
using Etherna.MongODM.Core.Conventions;
using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.ExecContext.AsyncLocal;
using Etherna.MongODM.Core.Extensions;
using Etherna.MongODM.Core.Models;
using Etherna.MongODM.Core.Options;
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.Core.Serialization.Mapping;
using Etherna.MongODM.Core.Serialization.Modifiers;
using Etherna.MongODM.Core.Serialization.Serializers;
using Etherna.MongODM.Core.Utility;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.MongODM.Core
{
    public class ModelMapSerializerTest
    {
        // Internal classes.
        public class DeserializationTestElement(
            BsonDocument document,
            FakeModel? expectedModel,
            Action<BsonReader>? preAction = null,
            Action<BsonReader>? postAction = null)
        {
            public BsonDocument Document { get; } = document;
            public FakeModel? ExpectedModel { get; } = expectedModel;
            public Action<BsonReader> PreAction { get; } = preAction ?? (_ => { });
            public Action<BsonReader> PostAction { get; } = postAction ?? (_ => { });
        }
        public class SerializationTestElement
        {
            public SerializationTestElement(
                FakeModel? model,
                BsonDocument expectedDocument,
                Action<BsonWriter>? preAction = null,
                Action<BsonWriter>? postAction = null)
            {
                BsonWriter = new BsonDocumentWriter(SerializedDocument);
                ExpectedDocument = expectedDocument;
                Model = model;
                PreAction = preAction ?? (_ => { });
                PostAction = postAction ?? (_ => { });
            }

            public BsonWriter BsonWriter { get; }
            public BsonDocument ExpectedDocument { get; }
            public FakeModel? Model { get; }
            public Action<BsonWriter> PreAction { get; }
            public Action<BsonWriter> PostAction { get; }
            public BsonDocument SerializedDocument { get; } = new();
        }

        public class DerivedFakeModel : FakeModel
        { }
        public class FakeModelProxy : FakeModel
        { }

        // Fields.
        private readonly Mock<IDbContextEngine> dbContextEngineMock = new();
        private readonly Mock<IDiscriminatorRegistry> discriminatorRegistryMock = new();
        private readonly Mock<IModelMap> modelMapMock = new();
        private readonly ModelMapSchemaIdOptions modelMapSchemaIdOptions = new();
        private readonly Mock<IMapRegistry> mapRegistryMock = new();
        private readonly Mock<ISerializerModifierAccessor> serializerModifierAccessorMock = new();

        // Constructor.
        public ModelMapSerializerTest()
        {
            discriminatorRegistryMock.Setup(r => r.LookupDiscriminatorConvention(It.IsAny<Type>()))
                .Returns(() => new HierarchicalProxyTolerantDiscriminatorConvention(dbContextEngineMock.Object, "_t"));

            dbContextEngineMock.Setup(c => c.DiscriminatorRegistry)
                .Returns(() => discriminatorRegistryMock.Object);
            dbContextEngineMock.Setup(c => c.ExecutionContext)
                .Returns(AsyncLocalContext.Instance);
            dbContextEngineMock.Setup(c => c.ProxyGenerator.IsProxyType(It.IsAny<Type>()))
                .Returns(true);
            dbContextEngineMock.Setup(c => c.ProxyGenerator.PurgeProxyType(It.IsAny<Type>()))
                .Returns<Type>(t => t);
            dbContextEngineMock.Setup(c => c.Options.ModelMapSchemaId)
                .Returns(() => modelMapSchemaIdOptions);
            dbContextEngineMock.Setup(c => c.MapRegistry)
                .Returns(() => mapRegistryMock.Object);
            dbContextEngineMock.Setup(c => c.SerializerModifierAccessor)
                .Returns(() => serializerModifierAccessorMock.Object);

            mapRegistryMock.Setup(sr => sr.GetModelMap(typeof(FakeModel)))
                .Returns(() => modelMapMock.Object);
        }

        // Data.
        public static IEnumerable<object[]> DeserializationTests
        {
            get
            {
                var tests = new List<DeserializationTestElement>
                {
                    // Null model
                    new(new BsonDocument(new BsonElement("elem", BsonNull.Value)),
                        null,
                        preAction: rd =>
                        {
                            rd.ReadStartDocument();
                            rd.ReadName();
                        },
                        postAction: rd => rd.ReadEndDocument()),

                    // Model without extra members
                    new(new BsonDocument(new BsonElement[]
                        {
                            new("_id", new BsonString("idVal")),
                            new("IntegerProp", new BsonInt32(8)),
                            new("StringProp", new BsonString("ok"))
                        } as IEnumerable<BsonElement>),
                        new FakeModel
                        {
                            Id = "idVal",
                            IntegerProp = 8,
                            StringProp = "ok"
                        })
                };
                return tests.Select(t => new object[] { t });
            }
        }

        // Tests.
        [Theory, MemberData(nameof(DeserializationTests))]
        public void Deserialize(DeserializationTestElement test)
        {
            // Setup
            var bsonReader = new BsonDocumentReader(test.Document);
            var classMap = new BsonClassMap<FakeModel>(cm => cm.AutoMap());
            classMap.Freeze();
            var serializer = new ModelMapSerializer<FakeModel>(dbContextEngineMock.Object);

            modelMapMock.Setup(s => s.ActiveSchema.Serializer)
                .Returns(classMap.ToSerializer());
            modelMapMock.Setup(s => s.ActiveSchema.FixDeserializedModelAsync(It.IsAny<object>()))
                .Returns<object>(Task.FromResult);

            // Action
            test.PreAction(bsonReader);
            var result = serializer.Deserialize(
                BsonDeserializationContext.CreateRoot(bsonReader),
                new BsonDeserializationArgs { NominalType = typeof(FakeModel) });
            test.PostAction(bsonReader);

            // Assert
            Assert.Equal(test.ExpectedModel, result, new FakeModelComparer());
        }

        [Fact]
        public void DeserializeClearsExtraElementsAfterModelFix()
        {
            /* MODM-3: unmapped document elements populate the extra elements bag, read by
             * the model fix during deserialization. After the fix the bag is emptied: a
             * loaded model doesn't carry the extra data around for its whole lifetime. */

            // Setup.
            var document = new BsonDocument(new BsonElement[]
            {
                new("_id", new BsonString("idVal")),
                new("StringProp", new BsonString("ok")),
                new("removedProp", new BsonString("extraVal"))
            } as IEnumerable<BsonElement>);
            var bsonReader = new BsonDocumentReader(document);
            var classMap = new BsonClassMap<FakeModel>(cm => cm.AutoMap());
            classMap.Freeze();
            var serializer = new ModelMapSerializer<FakeModel>(dbContextEngineMock.Object);

            //the fix reads the extra element, pinning its availability before the clear
            string? extraValueReadByFix = null;
            modelMapMock.Setup(s => s.ActiveSchema.Serializer)
                .Returns(classMap.ToSerializer());
            modelMapMock.Setup(s => s.ActiveSchema.FixDeserializedModelAsync(It.IsAny<object>()))
                .Returns<object>(m =>
                {
                    extraValueReadByFix = ((FakeModel)m).ExtraElements.TryGetExtraElementValue<string>("removedProp");
                    return Task.FromResult(m);
                });

            // Action.
            var result = serializer.Deserialize(
                BsonDeserializationContext.CreateRoot(bsonReader),
                new BsonDeserializationArgs { NominalType = typeof(FakeModel) });

            // Assert.
            Assert.NotNull(result);
            Assert.Equal("extraVal", extraValueReadByFix);
            Assert.NotNull(result.ExtraElements);
            Assert.Empty(result.ExtraElements);
        }

        [Fact]
        public void DeserializeFallsBackToActiveSchemaWithUnknownSchemaId()
        {
            // Setup.
            var document = new BsonDocument(new BsonElement[]
            {
                new("_s", new BsonString("unknownSchemaId")),
                new("_id", new BsonString("idVal")),
                new("StringProp", new BsonString("ok"))
            } as IEnumerable<BsonElement>);
            var bsonReader = new BsonDocumentReader(document);
            var classMap = new BsonClassMap<FakeModel>(cm => cm.AutoMap());
            classMap.Freeze();
            var serializer = new ModelMapSerializer<FakeModel>(dbContextEngineMock.Object);

            //no schema matches the id, and no fallback is configured
            modelMapMock.Setup(m => m.SchemasById)
                .Returns(new Dictionary<string, IModelMapSchema>());
            modelMapMock.Setup(s => s.ActiveSchema.Serializer)
                .Returns(classMap.ToSerializer());
            modelMapMock.Setup(s => s.ActiveSchema.FixDeserializedModelAsync(It.IsAny<object>()))
                .Returns<object>(Task.FromResult);

            // Action.
            var result = serializer.Deserialize(
                BsonDeserializationContext.CreateRoot(bsonReader),
                new BsonDeserializationArgs { NominalType = typeof(FakeModel) });

            // Assert.
            Assert.NotNull(result);
            Assert.Equal("idVal", result.Id);
            Assert.Equal("ok", result.StringProp);
        }

        [Fact]
        public void DeserializeHonorsFallbackSchemaWithUnknownSchemaId()
        {
            // Setup.
            var document = new BsonDocument(new BsonElement[]
            {
                new("_s", new BsonString("unknownSchemaId")),
                new("_id", new BsonString("idVal")),
                new("StringProp", new BsonString("ok"))
            } as IEnumerable<BsonElement>);
            var bsonReader = new BsonDocumentReader(document);
            var classMap = new BsonClassMap<FakeModel>(cm => cm.AutoMap());
            classMap.Freeze();
            var serializer = new ModelMapSerializer<FakeModel>(dbContextEngineMock.Object);

            //the fallback schema marks the deserialized model fixing a member value
            var fallbackSchemaMock = new Mock<IModelMapSchema>();
            fallbackSchemaMock.Setup(s => s.Serializer)
                .Returns(classMap.ToSerializer());
            fallbackSchemaMock.Setup(s => s.FixDeserializedModelAsync(It.IsAny<object>()))
                .Returns<object>(m =>
                {
                    ((FakeModel)m).IntegerProp = 42;
                    return Task.FromResult(m);
                });
            modelMapMock.Setup(m => m.SchemasById)
                .Returns(new Dictionary<string, IModelMapSchema>());
            modelMapMock.Setup(m => m.FallbackSchema)
                .Returns(fallbackSchemaMock.Object);
            modelMapMock.Setup(s => s.ActiveSchema.Serializer)
                .Returns(classMap.ToSerializer());

            // Action.
            var result = serializer.Deserialize(
                BsonDeserializationContext.CreateRoot(bsonReader),
                new BsonDeserializationArgs { NominalType = typeof(FakeModel) });

            // Assert.
            //the fallback schema deserialized and fixed the model
            Assert.NotNull(result);
            Assert.Equal("idVal", result.Id);
            Assert.Equal("ok", result.StringProp);
            Assert.Equal(42, result.IntegerProp);
        }

        [Fact]
        public void DeserializeHonorsFallbackSerializerWithUnknownSchemaId()
        {
            // Setup.
            var document = new BsonDocument(new BsonElement[]
            {
                new("_s", new BsonString("unknownSchemaId")),
                new("_id", new BsonString("idVal"))
            } as IEnumerable<BsonElement>);
            var bsonReader = new BsonDocumentReader(document);
            var classMap = new BsonClassMap<FakeModel>(cm => cm.AutoMap());
            classMap.Freeze();
            var serializer = new ModelMapSerializer<FakeModel>(dbContextEngineMock.Object);

            //the custom fallback serializer output is returned as is, with no fix applied
            var fallbackModel = new FakeModel { Id = "fallbackId" };
            var fallbackSerializerMock = new Mock<IBsonSerializer>();
            fallbackSerializerMock.Setup(s => s.Deserialize(It.IsAny<BsonDeserializationContext>(), It.IsAny<BsonDeserializationArgs>()))
                .Returns(fallbackModel);
            modelMapMock.Setup(m => m.SchemasById)
                .Returns(new Dictionary<string, IModelMapSchema>());
            modelMapMock.Setup(m => m.FallbackSerializer)
                .Returns(fallbackSerializerMock.Object);
            modelMapMock.Setup(s => s.ActiveSchema.Serializer)
                .Returns(classMap.ToSerializer());

            // Action.
            var result = serializer.Deserialize(
                BsonDeserializationContext.CreateRoot(bsonReader),
                new BsonDeserializationArgs { NominalType = typeof(FakeModel) });

            // Assert.
            Assert.Same(fallbackModel, result);
        }

        [Fact]
        public void DeserializeRegistersLoadedModelWithItsDocumentOnCurrentScope()
        {
            /* A full load inside a db context scope registers the fresh instance as the loaded
             * model of its document, capturing the model document from the just deserialized
             * document. */

            // Setup.
            var document = new BsonDocument(new BsonElement[]
            {
                new("_id", new BsonString("idVal")),
                new("StringProp", new BsonString("ok"))
            } as IEnumerable<BsonElement>);
            var bsonReader = new BsonDocumentReader(document);
            var classMap = new BsonClassMap<FakeModel>(cm => cm.AutoMap());
            classMap.Freeze();
            var serializer = new ModelMapSerializer<FakeModel>(dbContextEngineMock.Object);

            modelMapMock.Setup(s => s.ActiveSchema.Serializer)
                .Returns(classMap.ToSerializer());
            modelMapMock.Setup(s => s.ActiveSchema.FixDeserializedModelAsync(It.IsAny<object>()))
                .Returns<object>(Task.FromResult);

            var dbContextMock = new Mock<IDbContext>();
            dbContextMock.Setup(c => c.Engine)
                .Returns(dbContextEngineMock.Object);
            var repositoryMock = new Mock<IRepository>();
            repositoryMock.Setup(r => r.ModelType)
                .Returns(typeof(FakeModel));
            using var dbExecutionContext = new DbExecutionContextHandler(dbContextMock.Object, repositoryMock.Object);

            // Action.
            var result = serializer.Deserialize(
                BsonDeserializationContext.CreateRoot(bsonReader),
                new BsonDeserializationArgs { NominalType = typeof(FakeModel) });

            // Assert.
            Assert.NotNull(result);
            dbContextMock.Verify(c => c.RegisterLoadedModel("idVal", result), Times.Once());
            dbContextMock.Verify(c => c.SetModelBsonDocument(result, It.IsAny<BsonDocument>()), Times.Once());
        }

        [Fact]
        public void DeserializeReplacesOutdatedLoadedInstanceOnTypeChange()
        {
            /* The document changed type after the loaded instance materialized, and an instance
             * type can't upgrade: the fresh instance replaces the outdated one as the loaded
             * model, and is the returned one. */

            // Setup.
            var document = new BsonDocument(new BsonElement[]
            {
                new("_id", new BsonString("idVal")),
                new("StringProp", new BsonString("ok"))
            } as IEnumerable<BsonElement>);
            var bsonReader = new BsonDocumentReader(document);
            var classMap = new BsonClassMap<FakeModel>(cm => cm.AutoMap());
            classMap.Freeze();
            var serializer = new ModelMapSerializer<FakeModel>(dbContextEngineMock.Object);

            modelMapMock.Setup(s => s.ActiveSchema.Serializer)
                .Returns(classMap.ToSerializer());
            modelMapMock.Setup(s => s.ActiveSchema.FixDeserializedModelAsync(It.IsAny<object>()))
                .Returns<object>(Task.FromResult);

            var outdatedModel = new DerivedFakeModel { Id = "idVal" };
            var dbContextMock = new Mock<IDbContext>();
            dbContextMock.Setup(c => c.Engine)
                .Returns(dbContextEngineMock.Object);
            var repositoryMock = new Mock<IRepository>();
            repositoryMock.Setup(r => r.ModelType)
                .Returns(typeof(FakeModel));
            dbContextMock.Setup(c => c.TryGetLoadedModel(repositoryMock.Object, It.IsAny<object>()))
                .Returns(outdatedModel);
            using var dbExecutionContext = new DbExecutionContextHandler(dbContextMock.Object, repositoryMock.Object);

            // Action.
            var result = serializer.Deserialize(
                BsonDeserializationContext.CreateRoot(bsonReader),
                new BsonDeserializationArgs { NominalType = typeof(FakeModel) });

            // Assert.
            Assert.NotNull(result);
            Assert.NotSame(outdatedModel, result);
            dbContextMock.Verify(c => c.ReplaceOutdatedLoadedModel("idVal", outdatedModel, result), Times.Once());
            dbContextMock.Verify(c => c.SetModelBsonDocument(result, It.IsAny<BsonDocument>()), Times.Once());
        }

        [Theory]
        [InlineData("_s")]
        [InlineData("_m")]
        public void DeserializeResolvesSchemaIdFromItsDocumentElement(string schemaIdElementName)
        {
            /* MODM-153: the schema id element name is "_s"; documents written with the
             * previous "_m" name keep resolving their schema through the read fallback
             * names. The recognized element is removed before the schema deserialization:
             * an unremoved element would fail it, not matching any mapped member. */

            // Setup.
            var document = new BsonDocument(new BsonElement[]
            {
                new(schemaIdElementName, new BsonString("schemaId")),
                new("_id", new BsonString("idVal")),
                new("StringProp", new BsonString("ok"))
            } as IEnumerable<BsonElement>);
            var bsonReader = new BsonDocumentReader(document);
            var classMap = new BsonClassMap<FakeModel>(cm => cm.AutoMap());
            classMap.Freeze();
            var serializer = new ModelMapSerializer<FakeModel>(dbContextEngineMock.Object);

            //the resolved schema marks the deserialized model fixing a member value
            var schemaMock = new Mock<IModelMapSchema>();
            schemaMock.Setup(s => s.Serializer)
                .Returns(classMap.ToSerializer());
            schemaMock.Setup(s => s.FixDeserializedModelAsync(It.IsAny<object>()))
                .Returns<object>(m =>
                {
                    ((FakeModel)m).IntegerProp = 42;
                    return Task.FromResult(m);
                });
            modelMapMock.Setup(m => m.SchemasById)
                .Returns(new Dictionary<string, IModelMapSchema> { ["schemaId"] = schemaMock.Object });
            modelMapMock.Setup(s => s.ActiveSchema.Serializer)
                .Returns(classMap.ToSerializer());
            modelMapMock.Setup(s => s.ActiveSchema.FixDeserializedModelAsync(It.IsAny<object>()))
                .Returns<object>(Task.FromResult);

            // Action.
            var result = serializer.Deserialize(
                BsonDeserializationContext.CreateRoot(bsonReader),
                new BsonDeserializationArgs { NominalType = typeof(FakeModel) });

            // Assert.
            //the schema identified by the element deserialized and fixed the model
            Assert.NotNull(result);
            Assert.Equal("idVal", result.Id);
            Assert.Equal("ok", result.StringProp);
            Assert.Equal(42, result.IntegerProp);
        }

        [Fact]
        public void DeserializeReturnsAlreadyLoadedInstanceOfSameType()
        {
            /* One document materializes one instance inside a scope: a full load of a document
             * with an already loaded instance of the same type returns the existing one. */

            // Setup.
            var document = new BsonDocument(new BsonElement[]
            {
                new("_id", new BsonString("idVal")),
                new("StringProp", new BsonString("ok"))
            } as IEnumerable<BsonElement>);
            var bsonReader = new BsonDocumentReader(document);
            var classMap = new BsonClassMap<FakeModel>(cm => cm.AutoMap());
            classMap.Freeze();
            var serializer = new ModelMapSerializer<FakeModel>(dbContextEngineMock.Object);

            modelMapMock.Setup(s => s.ActiveSchema.Serializer)
                .Returns(classMap.ToSerializer());
            modelMapMock.Setup(s => s.ActiveSchema.FixDeserializedModelAsync(It.IsAny<object>()))
                .Returns<object>(Task.FromResult);

            var loadedModel = new FakeModel { Id = "idVal", StringProp = "already loaded" };
            var dbContextMock = new Mock<IDbContext>();
            dbContextMock.Setup(c => c.Engine)
                .Returns(dbContextEngineMock.Object);
            var repositoryMock = new Mock<IRepository>();
            repositoryMock.Setup(r => r.ModelType)
                .Returns(typeof(FakeModel));
            dbContextMock.Setup(c => c.TryGetLoadedModel(repositoryMock.Object, It.IsAny<object>()))
                .Returns(loadedModel);
            using var dbExecutionContext = new DbExecutionContextHandler(dbContextMock.Object, repositoryMock.Object);

            // Action.
            var result = serializer.Deserialize(
                BsonDeserializationContext.CreateRoot(bsonReader),
                new BsonDeserializationArgs { NominalType = typeof(FakeModel) });

            // Assert.
            Assert.Same(loadedModel, result);
            dbContextMock.Verify(c => c.RegisterLoadedModel(It.IsAny<object>(), It.IsAny<IEntityModel>()), Times.Never());
        }

        [Fact]
        public void GetDocumentId()
        {
            // Setup
            var model = new FakeModel { Id = "idVal" };
            var classMap = new BsonClassMap<FakeModel>(cm => cm.AutoMap());
            classMap.Freeze();
            var bsonClassMapSerializer = new BsonClassMapSerializer<FakeModel>(classMap);
            var serializer = new ModelMapSerializer<FakeModel>(dbContextEngineMock.Object);

            modelMapMock.Setup(s => s.ActiveSchema.Serializer)
                .Returns(() => classMap.ToSerializer());

            // Action
            var result = serializer.GetDocumentId(
                model,
                out object idResult,
                out Type idNominalTypeResult,
                out IIdGenerator idGeneratorResult);

            // Assert
            var resultExpected = bsonClassMapSerializer.GetDocumentId(
                model,
                out object idExpected,
                out Type idNominalTypeExpected,
                out IIdGenerator idGeneratorExpected);

            Assert.Equal(idExpected, idResult);
            Assert.Equal(idNominalTypeExpected, idNominalTypeResult);
            Assert.Equal(idGeneratorExpected, idGeneratorResult);
            Assert.Equal(resultExpected, result);
        }

        [Fact]
        public void GetMemberSerializationInfo()
        {
            // Setup
            var memberName = nameof(FakeModel.StringProp);
            var classMap = new BsonClassMap<FakeModel>(cm => cm.AutoMap());
            classMap.Freeze();
            var bsonClassMapSerializer = new BsonClassMapSerializer<FakeModel>(classMap);
            var serializer = new ModelMapSerializer<FakeModel>(dbContextEngineMock.Object);

            modelMapMock.Setup(s => s.ActiveSchema.Serializer)
                .Returns(() => classMap.ToSerializer());

            // Action
            var result = serializer.TryGetMemberSerializationInfo(memberName, out BsonSerializationInfo serializationInfo);

            // Assert
            var expectedResult = bsonClassMapSerializer.TryGetMemberSerializationInfo(memberName,
                out BsonSerializationInfo expectedSerializationInfo);
            Assert.Equal(expectedResult, result);
            Assert.Equal(expectedSerializationInfo.ElementName, serializationInfo.ElementName);
            Assert.Equal(expectedSerializationInfo.NominalType, serializationInfo.NominalType);
        }

        public static IEnumerable<object[]> SerializationTests
        {
            get
            {
                var tests = new List<SerializationTestElement>
                {
                    // Null model
                    new SerializationTestElement(
                        null,
                        new BsonDocument(new BsonElement("elem", BsonNull.Value)),
                        preAction: wr =>
                        {
                            wr.WriteStartDocument();
                            wr.WriteName("elem");
                        },
                        postAction: wr => wr.WriteEndDocument()),

                    // Complex model
                    new(new FakeModel
                        {
                            EnumerableProp = [new FakeModel(), null],
                            Id = "idVal",
                            IntegerProp = 42,
                            ObjectProp = new FakeModel(),
                            StringProp = "yes"
                        },
                        new BsonDocument(new BsonElement[]
                        {
                            new("_s", new BsonString("schemaId")),
                            new("_id", new BsonString("idVal")),
                            new("EnumerableProp", new BsonArray(
                            [
                                new BsonDocument(new BsonElement[]
                                {
                                    /*commented because serializer is not registered*/
                                    //new BsonElement("_s", new BsonString("schemaId")),
                                    new("_id", BsonNull.Value),
                                    new("EnumerableProp", BsonNull.Value),
                                    new("IntegerProp", new BsonInt32(0)),
                                    new("ObjectProp", BsonNull.Value),
                                    new("StringProp", BsonNull.Value)
                                } as IEnumerable<BsonElement>),
                                BsonNull.Value
                            ])),
                            new("IntegerProp", new BsonInt32(42)),
                            new("ObjectProp", new BsonDocument(new BsonElement[]
                            {
                                /*commented because serializer is not registered*/
                                //new BsonElement("_s", new BsonString("schemaId")),
                                new("_id", BsonNull.Value),
                                new("EnumerableProp", BsonNull.Value),
                                new("IntegerProp", new BsonInt32(0)),
                                new("ObjectProp", BsonNull.Value),
                                new("StringProp", BsonNull.Value)
                            } as IEnumerable<BsonElement>)),
                            new("StringProp", new BsonString("yes")),
                        } as IEnumerable<BsonElement>)),
                };

                return tests.Select(t => new object[] { t });
            }
        }

        [Theory, MemberData(nameof(SerializationTests))]
        public void Serialize(SerializationTestElement test)
        {
            // Setup
            var classMap = new BsonClassMap<FakeModel>(cm => cm.AutoMap());
            classMap.Freeze();
            var serializer = new ModelMapSerializer<FakeModel>(dbContextEngineMock.Object);

            modelMapMock.Setup(s => s.ActiveSchema.Serializer)
                .Returns(() => classMap.ToSerializer());
            modelMapMock.Setup(s => s.ActiveSchema.Id)
                .Returns("schemaId");

            mapRegistryMock.Setup(sr => sr.GetActiveSchemaIdBsonElement(typeof(FakeModel)))
                .Returns(new BsonElement("_s", new BsonString("schemaId")));

            // Action
            test.PreAction(test.BsonWriter);
            serializer.Serialize(
                BsonSerializationContext.CreateRoot(test.BsonWriter),
                new BsonSerializationArgs { NominalType = typeof(FakeModel) },
                test.Model!);
            test.PostAction(test.BsonWriter);

            // Assert
            Assert.Equal(0, test.SerializedDocument.CompareTo(test.ExpectedDocument));
        }

        [Fact]
        public void SerializeClearsExtraElementsBeforeWriting()
        {
            /* The persistence side protection of the extra data: whatever populated the bag,
             * a whole document write drops it, both from the model and from the document. */

            // Setup.
            var classMap = new BsonClassMap<FakeModel>(cm => cm.AutoMap());
            classMap.Freeze();
            var serializer = new ModelMapSerializer<FakeModel>(dbContextEngineMock.Object);

            modelMapMock.Setup(s => s.ActiveSchema.Serializer)
                .Returns(() => classMap.ToSerializer());
            mapRegistryMock.Setup(sr => sr.GetActiveSchemaIdBsonElement(typeof(FakeModel)))
                .Returns(new BsonElement("_s", new BsonString("schemaId")));
            dbContextEngineMock.Setup(c => c.ProxyGenerator.PurgeProxyType(typeof(FakeModelWithExtraElements)))
                .Returns(typeof(FakeModel));

            var model = new FakeModelWithExtraElements { Id = "idVal", StringProp = "ok" };
            model.SetExtraElements(new Dictionary<string, object> { ["removedProp"] = "extraVal" });

            var serializedDocument = new BsonDocument();
            using var bsonWriter = new BsonDocumentWriter(serializedDocument);

            // Action.
            serializer.Serialize(
                BsonSerializationContext.CreateRoot(bsonWriter),
                new BsonSerializationArgs { NominalType = typeof(FakeModel) },
                model);

            // Assert.
            Assert.NotNull(model.ExtraElements);
            Assert.Empty(model.ExtraElements);
            Assert.False(serializedDocument.Contains("removedProp"));
        }

        [Fact]
        public void SerializeProxyModelThroughItsPurgedModelMap()
        {
            /* MODM-189: proxy types have no registered model maps: a proxy instance serializes
             * through the model map of its purged type, writing the same document of a plain
             * instance - same members, same schema id, no proxy type traces. */

            // Setup.
            var classMap = new BsonClassMap<FakeModel>(cm => cm.AutoMap());
            classMap.Freeze();
            var serializer = new ModelMapSerializer<FakeModel>(dbContextEngineMock.Object);

            modelMapMock.Setup(s => s.ActiveSchema.Serializer)
                .Returns(() => classMap.ToSerializer());
            mapRegistryMock.Setup(sr => sr.GetActiveSchemaIdBsonElement(typeof(FakeModel)))
                .Returns(new BsonElement("_s", new BsonString("schemaId")));
            dbContextEngineMock.Setup(c => c.ProxyGenerator.PurgeProxyType(typeof(FakeModelProxy)))
                .Returns(typeof(FakeModel));

            BsonDocument SerializeModel(FakeModel model)
            {
                var document = new BsonDocument();
                using var bsonWriter = new BsonDocumentWriter(document);
                serializer.Serialize(
                    BsonSerializationContext.CreateRoot(bsonWriter),
                    new BsonSerializationArgs { NominalType = typeof(FakeModel) },
                    model);
                return document;
            }

            // Action.
            var proxyDocument = SerializeModel(new FakeModelProxy { Id = "idVal", IntegerProp = 42, StringProp = "yes" });
            var plainDocument = SerializeModel(new FakeModel { Id = "idVal", IntegerProp = 42, StringProp = "yes" });

            // Assert.
            Assert.Equal(0, proxyDocument.CompareTo(plainDocument));
        }

        [Fact]
        public void SetDocumentId()
        {
            // Setup
            var id = "idVal";
            var model = new FakeModel();
            var classMap = new BsonClassMap<FakeModel>(cm => cm.AutoMap());
            classMap.Freeze();
            var bsonClassMapSerializer = new BsonClassMapSerializer<FakeModel>(classMap);
            var serializer = new ModelMapSerializer<FakeModel>(dbContextEngineMock.Object);

            modelMapMock.Setup(s => s.ActiveSchema.Serializer)
                .Returns(() => classMap.ToSerializer());

            // Action
            serializer.SetDocumentId(model, id);

            // Assert
            var expectedModel = new FakeModel();
            bsonClassMapSerializer.SetDocumentId(expectedModel, id);
            Assert.Equal(expectedModel, model, new FakeModelComparer());
        }
    }
}
