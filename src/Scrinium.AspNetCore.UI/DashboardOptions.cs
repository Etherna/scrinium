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

using Etherna.Scrinium.AspNetCore.UI.Auth.Filters;
using System.Collections.Generic;

namespace Etherna.Scrinium.AspNetCore.UI
{
    public class DashboardOptions
    {
        // Properties.
        /// <summary>
        /// Target of the back link to the main application: a relative path, or an absolute
        /// http/https URL. Any other URL scheme is refused at registration, since the value
        /// renders as the href of the link. Set null to hide the link.
        /// </summary>
        public string? AppPath { get; set; } = "/";
        /// <summary>
        /// Filters authorizing an access to the dashboard. Access is granted when every one of them
        /// allows it, and denied by the first one denying it. An empty list leaves the dashboard
        /// unrestricted: an application with no authorization of its own declares it emptying the
        /// list, instead of configuring a filter allowing everyone.
        /// </summary>
        public IEnumerable<IDashboardAuthFilter> AuthFilters { get; set; } = [new LocalRequestsOnlyAuthFilter()];
        /// <summary>
        /// Route prefix of the dashboard pages. It replaces the area name as first route
        /// segment, and is normalized at registration: leading, trailing and repeated '/' name
        /// the same path. Left empty, it mounts the dashboard on the application root: what an
        /// application dedicated to it wants, and a collision with the pages of any other.
        /// </summary>
        public string BasePath { get; set; } = "Scrinium";
    }
}
