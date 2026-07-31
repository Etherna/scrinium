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

using Etherna.MongoDB.Driver;
using Etherna.MongODM.Core;
using Etherna.MongODM.Core.Repositories;
using Etherna.MongODM.Core.Serialization;
using Etherna.MongODM.IntegrationTests.ModelMaps;
using Etherna.MongODM.IntegrationTests.Models;
using System.Collections.Generic;

namespace Etherna.MongODM.IntegrationTests
{
    public interface ITestDbContext : IDbContext
    {
        IRepository<AccountBase, string> Accounts { get; }
        IRepository<Blog, string> ArchivedBlogs { get; }
        IRepository<Post, string> ArchivedPosts { get; }
        IRepository<Blog, string> Blogs { get; }
        IRepository<Bookmark, string> Bookmarks { get; }
        IRepository<Item, string> Items { get; }
        IRepository<Message, string> Messages { get; }
        IRepository<Post, string> Posts { get; }
        IRepository<Review, string> Reviews { get; }
        IRepository<TagBag, string> TagBags { get; }
    }

    internal sealed class TestDbContext : DbContext, ITestDbContext
    {
        // Properties.
        //repositories
        public IRepository<AccountBase, string> Accounts { get; } = new Repository<AccountBase, string>("accounts");
        public IRepository<Blog, string> ArchivedBlogs { get; } = new Repository<Blog, string>("archivedBlogs");
        public IRepository<Post, string> ArchivedPosts { get; } = new Repository<Post, string>("archivedPosts");
        public IRepository<Blog, string> Blogs { get; } = new Repository<Blog, string>(
            new RepositoryOptions<Blog>("blogs")
            {
                //custom index on a referenced document id path (MODM-98)
                IndexBuilders =
                [
                    (Builders<Blog>.IndexKeys.Ascending(b => b.LastPost!.Id),
                        new CreateIndexOptions<Blog> { Name = "blog_last_post" })
                ]
            });
        public IRepository<Bookmark, string> Bookmarks { get; } = new Repository<Bookmark, string>("bookmarks");
        public IRepository<Item, string> Items { get; } = new Repository<Item, string>("items");
        public IRepository<Message, string> Messages { get; } = new Repository<Message, string>("messages");
        public IRepository<Post, string> Posts { get; } = new Repository<Post, string>("posts");
        public IRepository<Review, string> Reviews { get; } = new Repository<Review, string>("reviews");
        public IRepository<TagBag, string> TagBags { get; } = new Repository<TagBag, string>("tagBags");

        // Protected properties.
        protected override IEnumerable<IModelMapsCollector> ModelMapsCollectors =>
            [new AccountMap(), new BlogMap(), new BookmarkMap(), new ItemMap(), new MessageMap(), new PostMap(), new ReviewMap(), new TagBagMap()];
    }
}
