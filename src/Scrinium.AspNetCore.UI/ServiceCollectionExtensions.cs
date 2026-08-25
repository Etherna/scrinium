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

using Etherna.Scrinium.AspNetCore.UI.Auth.Handlers;
using Etherna.Scrinium.AspNetCore.UI.Auth.Requirements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;

namespace Etherna.Scrinium.AspNetCore.UI
{
    public static class ServiceCollectionExtensions
    {
        private const string AreaName = "Scrinium";
        private const string FolderPath = "/";
        private const string PolicyName = "scriniumDashboardPolicy";

        public static IServiceCollection AddScriniumAdminDashboard(
            this IServiceCollection services,
            DashboardOptions? dashboardOptions = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            dashboardOptions ??= new DashboardOptions();

            /* Validate the back link target, which the dashboard layout renders as the href of
             * its back link: a relative path and an absolute http/https URL are accepted, while
             * any other scheme (javascript:, for one) would hand the operator clicking the link
             * a script running in the dashboard origin. The scheme reads like a browser reads
             * it, as the name before the first ':' of the space trimmed value, and control
             * characters are refused everywhere, since browsers discard them when parsing an
             * href, so they could disguise a scheme. */
            if (dashboardOptions.AppPath is { } appPath)
            {
                if (appPath.Any(char.IsControl))
                    throw new ArgumentException(
                        $"{nameof(DashboardOptions.AppPath)} contains control characters: " +
                        "the back link target must be a relative path, or an absolute http/https URL.",
                        nameof(dashboardOptions));

                var trimmedAppPath = appPath.Trim(' ');
                var schemeDelimiterIndex = trimmedAppPath.IndexOf(':', StringComparison.Ordinal);
                if (schemeDelimiterIndex >= 0)
                {
                    var scheme = trimmedAppPath[..schemeDelimiterIndex];
                    if (Uri.CheckSchemeName(scheme) &&
                        !scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                        !scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException(
                            $"{nameof(DashboardOptions.AppPath)} declares the \"{scheme}\" URL scheme: " +
                            "the back link target must be a relative path, or an absolute http/https URL.",
                            nameof(dashboardOptions));
                }
            }

            /* Normalize the base path, which replaces the area name as first route segment of
             * every dashboard page: leading, trailing and repeated '/' would render routes
             * carrying an empty segment, and every one of those values names the same path
             * anyway. A value left empty mounts the dashboard on the application root. */
            dashboardOptions.BasePath = string.Join('/', (dashboardOptions.BasePath ?? "").Split(
                '/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            // Register options for consumption from dashboard pages.
            services.AddSingleton(dashboardOptions);

            services.Configure<RazorPagesOptions>(options =>
            {
                options.Conventions.AuthorizeAreaFolder(AreaName, FolderPath, PolicyName);
                options.Conventions.AddAreaFolderRouteModelConvention(
                    AreaName, FolderPath,
                    routeModel =>
                    {
                        foreach (var selector in routeModel.Selectors)
                            if (selector.AttributeRouteModel?.Template is not null)
                            {
                                var segments = selector.AttributeRouteModel.Template.Split('/');
                                if (segments[0] == AreaName)
                                    segments[0] = dashboardOptions.BasePath;

                                selector.AttributeRouteModel.Template = string.Join("/", segments);
                            }
                    });
            });

            services.AddAuthorization(options =>
            {
                options.AddPolicy(PolicyName, policy =>
                {
                    policy.Requirements.Add(new ValidFiltersRequirement(dashboardOptions.AuthFilters));
                });
            });

            services.AddSingleton<IAuthorizationHandler, ValidFiltersHandler>();

            return services;
        }
    }
}
