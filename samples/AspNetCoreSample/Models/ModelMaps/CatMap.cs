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

using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.Extensions;
using Etherna.Scrinium.Core.Serialization;

namespace Etherna.Scrinium.AspNetCoreSample.Models.ModelMaps
{
    class CatMap : IModelMapsCollector
    {
        // Consts.
        public const string ActiveSchemaId = "cd37bafa-a36d-4b1f-815a-deb50c49d030";
        public const string PreviousSchemaId = "7f5c0b1e-2d3a-4c8e-9f10-5b6a7c8d9e0f";

        // Methods.
        public void Register(IDbContextEngine dbContextEngine)
        {
            var personSummarySerializer = PersonMap.SummarySerializer(dbContextEngine);

            // The secondary schema keeps loading the documents written before the active one.
            // Its documents count apart in the model schemas section of the admin dashboard,
            // and the db context migration rewrites them with the active schema.
            dbContextEngine.MapRegistry.AddModelMap<Cat>(ActiveSchemaId, schema =>
            {
                schema.AutoMap();
                schema.SetMemberSerializer(cat => cat.Owner!, personSummarySerializer);
            })
                .AddSecondarySchema(PreviousSchemaId, schema =>
                {
                    schema.AutoMap();
                    schema.SetMemberSerializer(cat => cat.Owner!, personSummarySerializer);
                });
        }
    }
}
