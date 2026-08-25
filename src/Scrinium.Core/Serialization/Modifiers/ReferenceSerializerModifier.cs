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

using Etherna.Scrinium.Core.ExecContext;
using Etherna.Scrinium.Core.ExecContext.Exceptions;
using Etherna.Scrinium.Core.Extensions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Etherna.Scrinium.Core.Serialization.Modifiers
{
    internal sealed class ReferenceSerializerModifier : IDisposable
    {
        // Consts.
        private const string ModifierKey = "ReferenceSerializerModifier";

        // Fields.
        private readonly ICollection<ReferenceSerializerModifier> requests;

        // Constructors and dispose.
        public ReferenceSerializerModifier(IExecutionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            requests = context.GetOrAddItemsList<ReferenceSerializerModifier>(ModifierKey);

            lock (((ICollection)requests).SyncRoot)
                requests.Add(this);
        }

        public void Dispose()
        {
            lock (((ICollection)requests).SyncRoot)
                requests.Remove(this);
        }

        // Properties.
        public bool ReadOnlyId { get; set; }

        // Static methods.
        public static bool IsReadOnlyIdEnabled(IExecutionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (context.Items is null)
                throw new ExecutionContextNotFoundException();

            var requests = context.TryGetItemsList<ReferenceSerializerModifier>(ModifierKey);
            if (requests is null)
                return false;

            lock (((ICollection)requests).SyncRoot)
                return requests.Any(r => r.ReadOnlyId);
        }
    }
}
