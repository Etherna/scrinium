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
        /// <summary>
        /// Filters authorizing an access to the dashboard. Access is granted when every one of them
        /// allows it, and denied by the first one denying it. An empty list leaves the dashboard
        /// unrestricted: an application with no authorization of its own declares it emptying the
        /// list, instead of configuring a filter allowing everyone.
        /// </summary>
        public IEnumerable<IDashboardAuthFilter> AuthFilters { get; set; } = [new LocalRequestsOnlyAuthFilter()];
        public string BasePath { get; set; } = "MongODM";
    }
}
