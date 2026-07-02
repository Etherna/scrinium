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

using Etherna.MongODM.AspNetCore.UI.Auth.Handlers;
using Etherna.MongODM.AspNetCore.UI.Auth.Requirements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Etherna.MongODM.AspNetCore.UI
{
    public static class ServiceCollectionExtensions
    {
        private const string AreaName = "MongODM";
        private const string FolderPath = "/";
        private const string PolicyName = "mongodmDashboardPolicy";

        public static IServiceCollection AddMongODMAdminDashboard(
            this IServiceCollection services,
            DashboardOptions? dashboardOptions = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            dashboardOptions ??= new DashboardOptions();

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
