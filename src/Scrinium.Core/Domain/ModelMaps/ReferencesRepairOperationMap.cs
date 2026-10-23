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
using Etherna.Scrinium.Core.Domain.Models;
using Etherna.Scrinium.Core.Domain.Models.ReferencesRepairOpAgg;
using Etherna.Scrinium.Core.Serialization;
using Etherna.Scrinium.Core.Serialization.Serializers;
using System;
using System.Collections.Generic;

namespace Etherna.Scrinium.Core.Domain.ModelMaps
{
    internal sealed class ReferencesRepairOperationMap : IModelMapsCollector
    {
        public void Register(IDbContextEngine dbContextEngine)
        {
            dbContextEngine.MapRegistry.AddModelMap<ReferencesRepairOperation>("9955882f-b262-44d3-9f8e-81c9d4b16acd", //0.25.8
                mm =>
                {
                    mm.AutoMap();

                    // Set dates representation.
                    mm.GetMemberMap(m => m.AbortRequestedDateTime).SetSerializer(
                        new NullableSerializer<DateTimeOffset>(new DateTimeOffsetSerializer(BsonType.DateTime)));
                    mm.GetMemberMap(m => m.CompletedDateTime).SetSerializer(
                        new NullableSerializer<DateTimeOffset>(new DateTimeOffsetSerializer(BsonType.DateTime)));
                })
                /* The schema of the operations written until 0.25.7, whose path states spell
                 * the succeeded state "Succeded": the same members, kept as a secondary schema
                 * so the rewrite of the deprecated schemas migrates those operations like any
                 * other document. Dropped in 1.0.0, with the deprecated schema id element. */
                .AddSecondarySchema("f2a7c1e4-63b9-4d08-9a35-7e0c4b8d1f62", //0.25.4
                    mm =>
                    {
                        mm.AutoMap();

                        // Set dates representation.
                        mm.GetMemberMap(m => m.AbortRequestedDateTime).SetSerializer(
                            new NullableSerializer<DateTimeOffset>(new DateTimeOffsetSerializer(BsonType.DateTime)));
                        mm.GetMemberMap(m => m.CompletedDateTime).SetSerializer(
                            new NullableSerializer<DateTimeOffset>(new DateTimeOffsetSerializer(BsonType.DateTime)));
                    });
            dbContextEngine.MapRegistry.AddModelMap<ReferencesRepairPathState>("6d3e0b95-84f1-42a7-b0c6-9f5a2d7e3b18", //0.25.4
                mm =>
                {
                    mm.AutoMap();

                    /* The path states of the operations on the secondary schema above spell
                     * the succeeded state "Succeded": read it under both names, write the
                     * current one, until the rewrite of the deprecated schemas has migrated
                     * them. Dropped in 1.0.0, with that schema. */
                    mm.GetMemberMap(m => m.State).SetSerializer(
                        new LegacyNamesEnumSerializer<ReferencesRepairPathState.ExecutionState>(
                            new Dictionary<string, ReferencesRepairPathState.ExecutionState>
                            {
                                ["Succeded"] = ReferencesRepairPathState.ExecutionState.Succeeded
                            }));
                });
        }
    }
}
