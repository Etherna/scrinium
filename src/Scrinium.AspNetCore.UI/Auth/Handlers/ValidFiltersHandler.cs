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

using Etherna.Scrinium.AspNetCore.UI.Auth.Requirements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace Etherna.Scrinium.AspNetCore.UI.Auth.Handlers
{
    internal sealed class ValidFiltersHandler(IHttpContextAccessor httpContextAccessor)
        : AuthorizationHandler<ValidFiltersRequirement>
    {
        // Protected methods.
        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            ValidFiltersRequirement requirement)
        {
            var httpContext = httpContextAccessor.HttpContext;

            /* Access is granted when every configured filter allows it, and denied by the first
             * one denying it. A dashboard configured without filters is unrestricted: an
             * application with no authorization of its own declares it emptying the list,
             * instead of configuring a filter allowing everyone. */
            foreach (var filter in requirement.Filters)
            {
                if (!await filter.AuthorizeAsync(httpContext).ConfigureAwait(false))
                {
                    context.Fail();
                    return;
                }
            }

            context.Succeed(requirement);
        }
    }
}
