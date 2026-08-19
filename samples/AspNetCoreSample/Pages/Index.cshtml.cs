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
using System.Linq;
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

            [Display(Name = "Owner")]
            public string? OwnerId { get; set; }
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
        public List<Cat> Cats { get; } = [];

        [BindProperty]
        public InputModel Input { get; set; }

        [BindProperty]
        [Display(Name = "Person name")]
        public string? NewPersonName { get; set; }

        public List<Person> Persons { get; } = [];

        // Methods.
        public async Task<IActionResult> OnGetAsync()
        {
            await LoadAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            await LoadAsync();

            if (!ModelState.IsValid)
                return Page();

            // Resolve the selected owner, if any.
            Person? owner = null;
            if (!string.IsNullOrEmpty(Input.OwnerId))
            {
                owner = await sampleDbContext.Persons.TryFindOneAsync(Input.OwnerId);
                if (owner is null)
                {
                    ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.OwnerId)}", "The selected owner doesn't exist anymore.");
                    return Page();
                }
            }

            var cat = new Cat(Input.Name, Input.Birthday, owner);
            await sampleDbContext.Cats.CreateAsync(cat);

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostAddPersonAsync()
        {
            if (string.IsNullOrWhiteSpace(NewPersonName))
            {
                /* Only the person name concerns this form: the cat form fields, bound empty by
                 * this post, must not render their validation errors. */
                ModelState.Clear();
                ModelState.AddModelError(nameof(NewPersonName), "The person name is required.");

                await LoadAsync();
                return Page();
            }

            var person = new Person(NewPersonName.Trim());
            await sampleDbContext.Persons.CreateAsync(person);

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostRemoveAsync(string id)
        {
            await sampleDbContext.Cats.DeleteAsync(id);

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostRemovePersonAsync(string id)
        {
            /* Deleting a person doesn't touch the cats referring them: their documents keep
             * the dangling summary, readable as the missing origin references that the admin
             * dashboard finds and removes. */
            await sampleDbContext.Persons.DeleteAsync(id);

            return RedirectToPage();
        }

        // Private helpers.
        private async Task LoadAsync()
        {
            var cats = await sampleDbContext.Cats.QueryElementsAsync(elements =>
                elements.ToListAsync());
            Cats.AddRange(cats);

            var persons = await sampleDbContext.Persons.QueryElementsAsync(elements =>
                elements.OrderBy(person => person.Name)
                        .ToListAsync());
            Persons.AddRange(persons);
        }
    }
}