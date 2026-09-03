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

using Etherna.Scrinium.Core.ExecContext;
using Etherna.Scrinium.Core.Extensions;
using System;
using System.Collections;
using System.Collections.Generic;

namespace Etherna.Scrinium.Core.Utility
{
    /// <summary>
    /// Marks the current execution flow as the one registering the maps of a db context engine.
    /// The conventions that Scrinium registers on the driver global convention registry apply only
    /// to the class maps built inside it, so any other type automapped in the process keeps the
    /// driver defaults.
    /// </summary>
    public sealed class MapsRegistrationHandler : IDisposable
    {
        // Consts.
        private const string HandlerKey = "MapsRegistrationHandler";

        // Fields.
        private readonly ICollection<MapsRegistrationHandler> requests;

        // Constructors and dispose.
        public MapsRegistrationHandler(IExecutionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            requests = context.GetOrAddItemsList<MapsRegistrationHandler>(HandlerKey);

            lock (((ICollection)requests).SyncRoot)
                requests.Add(this);
        }

        public void Dispose()
        {
            lock (((ICollection)requests).SyncRoot)
                requests.Remove(this);
        }

        // Static methods.
        /// <summary>
        /// Verify if the current execution flow is registering the maps of a db context engine.
        /// </summary>
        /// <param name="context">The execution context</param>
        /// <returns>True if maps are registering on the current flow</returns>
        public static bool IsRegisteringMaps(IExecutionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            /* Invoked by the driver while it looks up the conventions of a class map, from any
             * flow of the process: without an execution context there is no Scrinium registration
             * in progress, and reporting it is the answer, not an error. */
            var requests = context.TryGetItemsList<MapsRegistrationHandler>(HandlerKey);
            if (requests is null)
                return false;

            lock (((ICollection)requests).SyncRoot)
                return requests.Count != 0;
        }
    }
}
