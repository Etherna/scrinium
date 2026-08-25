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

using System;

namespace Etherna.Scrinium.AspNetCoreSample.Models
{
    public class Cat : EntityModelBase<string>
    {
        public Cat(string name, DateTime birthday, Person? owner = null)
        {
            Name = name;
            Birthday = birthday;
            Owner = owner;
        }
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        protected Cat() { }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        public virtual int Age => (int)((DateTime.Now - Birthday).TotalDays / 365);
        public virtual DateTime Birthday { get; protected set; }
        public virtual string Name { get; protected set; }

        // The owner is referred, not embedded: its document lives in its own collection, and
        // the cat document carries only the summary declared by the reference serializer.
        public virtual Person? Owner { get; protected set; }
    }
}
