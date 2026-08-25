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

using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.Repositories;
using Etherna.Scrinium.Core.Serialization;
using Etherna.Scrinium.IntegrationTests.Models;
using System.Collections.Generic;

namespace Etherna.Scrinium.IntegrationTests
{
    /// <summary>
    /// A misconfigured db context: the Blog model map embeds its Post members as full
    /// documents instead of referencing them. Initialization must fail fast.
    /// </summary>
    internal sealed class InvalidEmbeddedEntityDbContext : DbContext
    {
        // Properties.
        //repositories
        public IRepository<Blog, string> Blogs { get; } = new Repository<Blog, string>("invalidEmbeddedBlogs");
        public IRepository<Post, string> Posts { get; } = new Repository<Post, string>("invalidEmbeddedPosts");

        // Protected properties.
        protected override IEnumerable<IModelMapsCollector> ModelMapsCollectors =>
            [new InvalidEmbeddedBlogMap()];

        // Helpers.
        private sealed class InvalidEmbeddedBlogMap : IModelMapsCollector
        {
            public void Register(IDbContextEngine dbContextEngine)
            {
                dbContextEngine.MapRegistry.AddModelMap<Post>(
                    "84e72393-c584-4223-895d-ec9cb190e3d7");

                //no reference serializers on the entity model members: invalid configuration
                dbContextEngine.MapRegistry.AddModelMap<Blog>(
                    "2a6f9d23-0d59-4295-abbd-fac13b4792bf");
            }
        }
    }
}
