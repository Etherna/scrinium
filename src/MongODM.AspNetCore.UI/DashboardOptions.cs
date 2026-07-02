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

using Etherna.MongODM.AspNetCore.UI.Auth.Filters;
using System.Collections.Generic;

namespace Etherna.MongODM.AspNetCore.UI
{
    public class DashboardOptions
    {
        // Properties.
        /// <summary>
        /// Path or URL of the back link to the main application. Set null to hide the link.
        /// </summary>
        public string? AppPath { get; set; } = "/";
        public IEnumerable<IDashboardAuthFilter> AuthFilters { get; set; } = [new LocalRequestsOnlyAuthFilter()];
        public string BasePath { get; set; } = "MongODM";
    }
}
