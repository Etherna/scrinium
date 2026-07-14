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

using Etherna.MongODM.Core.Attributes;
using Etherna.MongODM.Core.Domain.Models;
using System.Collections.Generic;

namespace Etherna.MongODM.IntegrationTests.Models
{
    public class Blog : EntityModelBase<string>
    {
        // Fields.
        private List<Post> _posts = [];

        // Constructors.
        public Blog(string title)
        {
            Title = title;
        }
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        protected Blog() { }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        // Properties.
        public virtual Post? LastPost { get; protected set; }
        public virtual IEnumerable<Post> Posts
        {
            get => _posts;
            protected set => _posts = [.. value ?? []];
        }
        public virtual string Title { get; set; }

        // Methods.
        [PropertyAlterer(nameof(LastPost))]
        [PropertyAlterer(nameof(Posts))]
        public virtual void AddPost(Post post)
        {
            _posts.Add(post);
            LastPost = post;
        }
    }
}
