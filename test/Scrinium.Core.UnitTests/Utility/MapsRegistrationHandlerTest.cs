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

using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Etherna.Scrinium.Core.ExecContext.Exceptions;
using Xunit;

namespace Etherna.Scrinium.Core.Utility
{
    public class MapsRegistrationHandlerTest
    {
        // Tests.
        [Fact]
        public void HandlerMarksOnlyItsOwnScope()
        {
            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            // Action and assert.
            Assert.False(MapsRegistrationHandler.IsRegisteringMaps(AsyncLocalContext.Instance));

            using (new MapsRegistrationHandler(AsyncLocalContext.Instance))
                Assert.True(MapsRegistrationHandler.IsRegisteringMaps(AsyncLocalContext.Instance));

            Assert.False(MapsRegistrationHandler.IsRegisteringMaps(AsyncLocalContext.Instance));
        }

        [Fact]
        public void MissingExecutionContextRegistersNoMaps()
        {
            /* The convention filter runs on any flow of the process building a class map,
             * MongODM ones included: without an execution context there is no registration
             * in progress, and only the handler construction is an error. */

            // Setup.
            Assert.Null(AsyncLocalContext.Instance.Items);

            // Action and assert.
            Assert.False(MapsRegistrationHandler.IsRegisteringMaps(AsyncLocalContext.Instance));
            Assert.Throws<ExecutionContextNotFoundException>(
                () => new MapsRegistrationHandler(AsyncLocalContext.Instance));
        }
    }
}
