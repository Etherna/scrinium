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
using Etherna.MongoDB.Bson.Serialization.Serializers;
using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.Serialization;
using Etherna.Scrinium.Core.Serialization.Serializers;

namespace Etherna.Scrinium.AspNetCoreSample.Models.ModelMaps
{
    class PersonMap : IModelMapsCollector
    {
        // Consts.
        public const string ActiveSchemaId = "b3f0a4d6-6c1e-4a2f-9a53-1c0d7e9f2b48";
        private const string SummaryBaseSchemaId = "8d4c7e21-3b95-4f68-8c07-2ad9e6b1f374";
        private const string SummaryEntityBaseSchemaId = "5e2a9f43-71d6-4b80-9c15-3f8ab0d4e267";
        private const string SummarySchemaId = "0f6b2c19-5a84-4d3e-91b7-6ac4e2d8f105";

        // Methods.
        public void Register(IDbContextEngine dbContextEngine)
        {
            dbContextEngine.MapRegistry.AddModelMap<Person>(ActiveSchemaId);
        }

        /// <summary>
        /// The person summary denormalized into the documents referring them: the name travels
        /// with the reference, so reading it doesn't load the person document. The document
        /// dependencies section of the admin dashboard reports where this summary lands, and
        /// renaming a person rewrites it in every cat referring them.
        /// The source repository is not declared: both the writable and the read-only sample
        /// db contexts declare a single persons repository, resolved at engine build.
        /// </summary>
        public static ReferenceSerializer<Person, string> SummarySerializer(IDbContextEngine dbContextEngine) =>
            new(dbContextEngine, config =>
            {
                /* By default, deleting a person through their repository removes this
                 * reference from every cat referring them, in background: the owner becomes
                 * null. Uncomment to cascade instead: the delete then deletes the referring
                 * cats themselves, with a domain delete propagating their own reference
                 * policies in turn. */
                // config.OriginDelete = Etherna.Scrinium.Core.Options.OriginDeleteMode.DeleteReferencingDocument;

                config.AddModelMap<ModelBase>(SummaryBaseSchemaId, _ => { }); //no summary members at this level
                config.AddModelMap<EntityModelBase<string>>(SummaryEntityBaseSchemaId, schema =>
                {
                    schema.MapIdMember(model => model.Id); //every summary carries the reference id
                    schema.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId));
                });
                config.AddModelMap<Person>(SummarySchemaId, schema => schema.MapMember(person => person.Name));
            });
    }
}
