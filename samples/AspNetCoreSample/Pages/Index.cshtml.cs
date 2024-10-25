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

using Etherna.MongoDB.Driver.Linq;
using Etherna.MongODM.AspNetCoreSample.Models;
using Etherna.MongODM.AspNetCoreSample.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace Etherna.MongODM.AspNetCoreSample.Pages
{
    public class IndexModel : PageModel
    {
        // Models.
        public class InputModel
        {
            [Required]
            [DataType(DataType.Date)]
            public DateTime Birthday { get; set; }

            [Required]
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
            public string Name { get; set; }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        }

        // Fields.
        private readonly ISampleDbContext sampleDbContext;

        // Constructor.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public IndexModel(ISampleDbContext sampleDbContext)
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        {
            this.sampleDbContext = sampleDbContext;
        }

        // Properties.
        public List<Cat> Cats { get; } = new List<Cat>();

        [BindProperty]
        public InputModel Input { get; set; }

        // Methods.
        public async Task<IActionResult> OnGetAsync()
        {
            await LoadCatsAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            await LoadCatsAsync();

            if (!ModelState.IsValid)
                return Page();

            var cat = new Cat(Input.Name, Input.Birthday);
            await sampleDbContext.Cats.CreateAsync(cat);

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostRemoveAsync(string id)
        {
            await sampleDbContext.Cats.DeleteAsync(id);

            return RedirectToPage();
        }

        // Private helpers.
        private async Task LoadCatsAsync()
        {
            var cats = await sampleDbContext.Cats.QueryElementsAsync(elements =>
                elements.ToListAsync());

            Cats.AddRange(cats);
        }
    }
}