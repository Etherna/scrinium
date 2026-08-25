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

using Etherna.MongODM.AspNetCore;
using Etherna.MongODM.AspNetCore.Extensions;
using Etherna.MongODM.Core.Options;
using Etherna.MongODM.Core.ProxyModels;
using Etherna.MongODM.HF.Tasks;
using Hangfire;
using Hangfire.Mongo;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Etherna.MongODM.Extensions
{
    public static class ServiceCollectionExtensions
    {
        // Methods.
        public static IMongODMConfiguration AddMongODMWithHangfire(
            this IServiceCollection services,
            Action<HangfireOptions>? configureHangfireOptions = null,
            Action<MongODMOptions>? configureMongODMOptions = null)
        {
            // Configure MongODM.
            var conf = services.AddMongODM<HangfireTaskRunner>(configureMongODMOptions);

            // Configure Hangfire.
            var hangfireOptions = new HangfireOptions();
            configureHangfireOptions?.Invoke(hangfireOptions);

            services.AddHangfire(options =>
            {
                options.UseMongODM();
                options.UseMongoStorage(hangfireOptions.ConnectionString, hangfireOptions.StorageOptions);
            });

            return conf;
        }
    }
}
