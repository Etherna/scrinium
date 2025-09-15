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

using Etherna.MongODM.Core;
using Etherna.MongODM.Core.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Etherna.MongODM.AspNetCore.UI.Areas.MongODM.Pages
{
    [IgnoreAntiforgeryToken]
    public class IndexModel : PageModel
    {
        // Fields.
        private readonly MongODMOptions options;
        private readonly IServiceProvider serviceProvider;

        // Constructor.
        public IndexModel(
            IOptions<MongODMOptions> options,
            IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(options, nameof(options));

            this.options = options.Value;
            this.serviceProvider = serviceProvider;
        }

        // Properties.
        public IEnumerable<IDbContext> DbContexts { get; private set; } = null!;

        // Methods.
        public void OnGet()
        {
            InitializePage();
        }

        public async Task OnPostAsync(string identifier)
        {
            InitializePage();

            // Find dbcontext.
            var dbcontext = DbContexts.First(dbc => dbc.Identifier == identifier);

            // Migrate.
            await dbcontext.StartMigrationAsync().ConfigureAwait(false);
        }

        // Helpers.
        private void InitializePage()
        {
            // Get dbcontext instances.
            var dbContextTypes = options.DbContextTypes;
            DbContexts = dbContextTypes.Select(type => (IDbContext)serviceProvider.GetRequiredService(type));
        }
    }
}
