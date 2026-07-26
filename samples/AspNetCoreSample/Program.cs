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

using Etherna.MongODM.AspNetCore.Extensions;
using Etherna.MongODM.AspNetCore.UI;
using Etherna.MongODM.AspNetCoreSample.Persistence;
using Etherna.MongODM.Extensions;
using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Etherna.MongODM.AspNetCoreSample
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            /* Serve the build-time static web assets (e.g. the scoped css bundle) in any
             * environment: by default they load only on Development, and the sample can
             * run as a plain non-published binary. */
            builder.WebHost.UseStaticWebAssets();

            // Add services to the container.
            builder.Services.AddRazorPages();

            builder.Services.AddHangfireServer();

            // Connection strings come from configuration (see appsettings*.json).
            var hangfireDbConnectionString = builder.Configuration.GetConnectionString("HangfireDb")
                ?? throw new InvalidOperationException("Missing ConnectionStrings:HangfireDb configuration");
            var sampleDbConnectionString = builder.Configuration.GetConnectionString("SampleDb")
                ?? throw new InvalidOperationException("Missing ConnectionStrings:SampleDb configuration");

            builder.Services.AddMongODMWithHangfire(hangfireOptions =>
                {
                    hangfireOptions.ConnectionString = hangfireDbConnectionString;
                })
                .AddDbContext<ISampleDbContext, SampleDbContext>(options =>
                {
                    options.ConnectionString = sampleDbConnectionString;
                })
                //read-only view over the same database, to demo read-only db context access
                .AddDbContext<IReadOnlySampleDbContext, ReadOnlySampleDbContext>(options =>
                {
                    options.ConnectionString = sampleDbConnectionString;
                    options.IsReadOnly = true;
                });

            builder.Services.AddMongODMAdminDashboard();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            app.UseHttpsRedirection();

            app.UseDeveloperExceptionPage();

            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.UseHangfireDashboard();

            app.MapRazorPages();

            app.SeedDbContexts();

            app.Run();
        }
    }
}