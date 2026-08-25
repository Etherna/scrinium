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

using Etherna.Scrinium.AspNetCore;
using Etherna.Scrinium.AspNetCore.Extensions;
using Etherna.Scrinium.Core.Options;
using Etherna.Scrinium.HF.Tasks;
using Hangfire;
using Hangfire.Mongo;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Etherna.Scrinium.Extensions
{
    public static class ServiceCollectionExtensions
    {
        // Methods.
        public static IScriniumConfiguration AddScriniumWithHangfire(
            this IServiceCollection services,
            Action<HangfireOptions>? configureHangfireOptions = null,
            Action<ScriniumOptions>? configureScriniumOptions = null)
        {
            // Configure MongODM.
            var conf = services.AddScrinium<HangfireTaskRunner>(configureScriniumOptions);

            // Configure Hangfire.
            var hangfireOptions = new HangfireOptions();
            configureHangfireOptions?.Invoke(hangfireOptions);

            services.AddHangfire(options =>
            {
                options.UseScrinium();
                options.UseMongoStorage(hangfireOptions.ConnectionString, hangfireOptions.StorageOptions);
            });

            return conf;
        }
    }
}
