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

using Microsoft.AspNetCore.Http;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.MongODM.AspNetCore.UI.Auth.Filters
{
    public class LocalRequestsOnlyAuthFilterTest
    {
        // Fields.
        private readonly LocalRequestsOnlyAuthFilter filter = new();

        // Tests.
        [Fact]
        public async Task AuthorizeAllowsClientOnLocalAddress()
        {
            // Setup.
            var context = new DefaultHttpContext();
            context.Connection.LocalIpAddress = IPAddress.Parse("10.0.0.5");
            context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");

            // Action.
            var isAuthorized = await filter.AuthorizeAsync(context);

            // Assert.
            Assert.True(isAuthorized);
        }

        [Theory]
        [InlineData("127.0.0.1")]
        [InlineData("::1")]
        [InlineData("::ffff:127.0.0.1")]
        public async Task AuthorizeAllowsLoopbackClient(string remoteAddress)
        {
            // Setup.
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);

            // Action.
            var isAuthorized = await filter.AuthorizeAsync(context);

            // Assert.
            Assert.True(isAuthorized);
        }

        [Theory]
        [InlineData("Forwarded", "for=203.0.113.9")]
        [InlineData("X-Forwarded-For", "203.0.113.9")]
        [InlineData("X-Forwarded-For", "127.0.0.1")]
        [InlineData("X-Forwarded-Host", "example.com")]
        [InlineData("X-Forwarded-Prefix", "/app")]
        [InlineData("X-Forwarded-Proto", "https")]
        [InlineData("X-Original-For", "203.0.113.9")]
        [InlineData("X-Original-Host", "example.com")]
        [InlineData("X-Original-Prefix", "/app")]
        [InlineData("X-Original-Proto", "https")]
        [InlineData("X-Real-IP", "203.0.113.9")]
        public async Task AuthorizeDeniesForwardedRequestsAlsoWithLoopbackPeer(
            string headerName,
            string headerValue)
        {
            // Setup.
            var context = new DefaultHttpContext();
            context.Connection.LocalIpAddress = IPAddress.Loopback;
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            context.Request.Headers[headerName] = headerValue;

            // Action.
            var isAuthorized = await filter.AuthorizeAsync(context);

            // Assert.
            Assert.False(isAuthorized);
        }

        [Fact]
        public async Task AuthorizeDeniesNullContext()
        {
            // Action.
            var isAuthorized = await filter.AuthorizeAsync(null);

            // Assert.
            Assert.False(isAuthorized);
        }

        [Fact]
        public async Task AuthorizeDeniesRemoteClient()
        {
            // Setup.
            var context = new DefaultHttpContext();
            context.Connection.LocalIpAddress = IPAddress.Parse("10.0.0.5");
            context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");

            // Action.
            var isAuthorized = await filter.AuthorizeAsync(context);

            // Assert.
            Assert.False(isAuthorized);
        }

        [Fact]
        public async Task AuthorizeDeniesUnknownRemoteAddress()
        {
            // Setup.
            var context = new DefaultHttpContext();

            // Action.
            var isAuthorized = await filter.AuthorizeAsync(context);

            // Assert.
            Assert.False(isAuthorized);
        }
    }
}
