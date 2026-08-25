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

using System;

namespace Etherna.Scrinium.Core.Utility
{
    /* The in flight span of one guarded operation: disposed when the operation completes,
     * exiting the count it entered. An uncounted scope closes nothing: the span of an
     * operation admitted by an exclusive access allowance, meant to work during the
     * window and so out of its drain. */
    internal readonly struct InFlightOperationScope(
        InFlightOperationsCounter counter,
        bool isWriteOperation,
        bool isCounted)
        : IDisposable
    {
        public void Dispose()
        {
            if (!isCounted)
                return;

            if (isWriteOperation)
                counter.ExitWrite();
            else
                counter.ExitRead();
        }
    }
}
