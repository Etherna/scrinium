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
using Etherna.MongODM.AspNetCoreSample.Models;
using Etherna.MongODM.AspNetCoreSample.Models.ModelMaps;
using Etherna.MongODM.Core;
using Etherna.MongODM.Core.Migration;
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.Core.Serialization;
using Etherna.MongODM.Core.Serialization.Mapping;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Etherna.MongODM.AspNetCoreSample.Persistence
{
    public class SampleDbContext : DbContext, ISampleDbContext
    {
        public IRepository<Cat, string> Cats { get; } = new Repository<Cat, string>("cats");
        public IRepository<Person, string> Persons { get; } = new Repository<Person, string>("persons");

        // Rewrite each cat document with the active schema. Try it from the admin dashboard,
        // also as dry run: failing documents report into the operation logs without persisting.
        public override IEnumerable<DocumentMigration> DocumentMigrationList =>
            [new DocumentMigration<Cat, string>(Cats)];

        protected override IEnumerable<IModelMapsCollector> ModelMapsCollectors =>
            [new ModelBaseMap(), new CatMap(), new PersonMap()];

        protected override async Task SeedAsync()
        {
            /* Seed the persons owning the cats. Each cat document denormalizes the owner
             * name beside the reference id: the document dependencies section of the admin
             * dashboard reports the path carrying it, and renaming a person rewrites it in
             * every cat referring them. */
            var alice = new Person("Alice");
            var bob = new Person("Bob");
            await Persons.CreateAsync([alice, bob]);

            // Seed cats written with the active model map schema.
            await Cats.CreateAsync([
                new Cat("Kitty", new DateTime(2021, 3, 14, 0, 0, 0, DateTimeKind.Utc), alice),
                new Cat("Tom", new DateTime(2019, 7, 2, 0, 0, 0, DateTimeKind.Utc), bob),
                new Cat("Milo", new DateTime(2022, 9, 30, 0, 0, 0, DateTimeKind.Utc), alice)
            ]);

            /* Seed also cats as written by a previous version of the application: create them
             * like any other, then stamp their documents with the previous schema id. The model
             * schemas section of the admin dashboard counts them on the deprecated schema, and
             * running a migration rewrites them with the active one. */
            Cat[] previousSchemaCats = [
                new Cat("Felix", new DateTime(2014, 5, 20, 0, 0, 0, DateTimeKind.Utc), bob),
                new Cat("Garfield", new DateTime(2016, 11, 8, 0, 0, 0, DateTimeKind.Utc), alice)
            ];
            await Cats.CreateAsync(previousSchemaCats);

            await Engine.Database.GetCollection<BsonDocument>(Cats.Name).UpdateManyAsync(
                Builders<BsonDocument>.Filter.In("_id", previousSchemaCats.Select(cat => ObjectId.Parse(cat.Id))),
                Builders<BsonDocument>.Update.Set(
                    ModelMapSchema.IdElementName,
                    CatMap.PreviousSchemaId));

            await base.SeedAsync();
        }
    }
}
