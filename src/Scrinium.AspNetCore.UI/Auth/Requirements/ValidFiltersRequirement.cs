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
using Microsoft.AspNetCore.Authorization;
using System;
using System.Collections.Generic;

namespace Etherna.Scrinium.AspNetCore.UI.Auth.Requirements
{
    internal sealed class ValidFiltersRequirement : IAuthorizationRequirement
    {
        // Constructor.
        public ValidFiltersRequirement(IEnumerable<IDashboardAuthFilter> filters)
        {
            ArgumentNullException.ThrowIfNull(filters);

            Filters = [.. filters];
        }

        // Properties.
        /// <summary>
        /// The filters authorizing an access to the dashboard, snapshot when the policy is built:
        /// every request of the application resolves the same ones.
        /// </summary>
        public IReadOnlyList<IDashboardAuthFilter> Filters { get; }
    }
}
