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
    /* Marks the current execution flow as the write of a reference document: the summary
     * serialization reads every member its schema declares, so a member the stored summary
     * doesn't carry loads the origin document. That load is a precondition of a correct
     * write, not an application read: the implicit lazy load option never denies it, and a
     * missing origin document denies the write instead of degrading it to default values. */
    internal sealed class ReferenceWriteHandler : IDisposable
    {
        // Consts.
        private const string HandlerKey = "ReferenceWriteHandler";

        // Fields.
        private readonly ICollection<ReferenceWriteHandler> requests;

        // Constructors and dispose.
        private ReferenceWriteHandler(IExecutionContext context)
        {
            requests = context.GetOrAddItemsList<ReferenceWriteHandler>(HandlerKey);

            lock (((ICollection)requests).SyncRoot)
                requests.Add(this);
        }

        public void Dispose()
        {
            lock (((ICollection)requests).SyncRoot)
                requests.Remove(this);
        }

        // Static methods.
        public static bool IsWritingReference(IExecutionContext context)
        {
            /* Invoked also from flows without an execution context, like any serialization run
             * outside a Scrinium scope: such a flow carries no reference write, and reporting
             * it is the answer, not an error. */
            var requests = context.TryGetItemsList<ReferenceWriteHandler>(HandlerKey);
            if (requests is null)
                return false;

            lock (((ICollection)requests).SyncRoot)
                return requests.Count != 0;
        }

        /// <summary>
        /// Enter the reference write scope on the execution context, or nothing when the flow
        /// has none: a serialization running outside a Scrinium scope has no context carrying
        /// the scope, and no db operation completing a summary from it.
        /// </summary>
        public static IDisposable? TryEnter(IExecutionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            return context.Items is null ? null : new ReferenceWriteHandler(context);
        }
    }
}
