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

using System.Threading;

namespace Etherna.Scrinium.Core.Utility
{
    /* Counts the operations in flight on the guarded collections of an engine, reads and
     * writes apart, letting the exclusive access window drain the operations admitted
     * before it opened. Enters and counts reads are full fences: paired with the enter
     * running before the guard reads the exclusive flags, an operation admitted with the
     * flags off is always visible to a count read after the flags flip, so it either gets
     * denied or gets drained — never runs unseen beside the exclusive work. */
    internal sealed class InFlightOperationsCounter
    {
        // Fields.
        private int readsCount;
        private int writesCount;

        // Properties.
        public int ReadsCount => Interlocked.CompareExchange(ref readsCount, 0, 0);
        public int WritesCount => Interlocked.CompareExchange(ref writesCount, 0, 0);

        // Methods.
        public void EnterRead() => Interlocked.Increment(ref readsCount);
        public void EnterWrite() => Interlocked.Increment(ref writesCount);
        public void ExitRead() => Interlocked.Decrement(ref readsCount);
        public void ExitWrite() => Interlocked.Decrement(ref writesCount);
    }
}
