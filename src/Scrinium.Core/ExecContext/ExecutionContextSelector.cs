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
using System.Collections.Generic;

namespace Etherna.Scrinium.Core.ExecContext
{
    /// <summary>
    ///     A multi context selector that take different contexts, and select the first available.
    /// </summary>
    /// <remarks>
    ///     This class is intended to have the same lifetime of its consumer. For example, in case
    ///     of using with a DbContext, the same DbContext instance will use the same ContextSelector
    ///     instance. This mean that if a DbContext is running over different execution contexts,
    ///     every <see cref="Items"/> invoke on same context needs to return the same dictionary.
    ///     The simplest way to perform this, is to return the first not null available dictionary
    ///     on subscribed contexts.
    /// </remarks>
    public class ExecutionContextSelector(IEnumerable<IExecutionContext> contexts)
        : IExecutionContext
    {
        // Fields.
        private readonly IEnumerable<IExecutionContext> contexts =
            contexts ?? throw new ArgumentNullException(nameof(contexts));

        // Properties.
        public IDictionary<object, object?>? Items
        {
            get
            {
                foreach (var context in contexts)
                    if (context.Items != null)
                        return context.Items;
                return null;
            }
        }
    }
}
