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

using Etherna.MongODM.Core.Domain.Models;
using System.Collections.Generic;

namespace Etherna.MongODM.IntegrationTests.Models
{
    public class Message : EntityModelBase<string>
    {
        // Fields.
        private List<AccountBase> _watchers = [];

        // Constructors.
        public Message(string text, AccountBase author, AccountBase editor)
        {
            Text = text;
            Author = author;
            Editor = editor;
        }
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        protected Message() { }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        // Properties.
        public virtual AccountBase Author { get; protected set; }
        public virtual IEnumerable<Envelope> Batches { get; set; } = [];
        public virtual Dispatch? Dispatch { get; set; }
        public virtual AccountBase Editor { get; set; }
        public virtual Envelope? Envelope { get; set; }
        public virtual string Text { get; set; }
        public virtual IEnumerable<AccountBase> Watchers
        {
            get => _watchers;
            protected set => _watchers = [.. value ?? []];
        }

        // Methods.
        public virtual void AddWatcher(AccountBase watcher) =>
            _watchers.Add(watcher);
    }
}
