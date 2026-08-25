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

using Microsoft.AspNetCore.Http;
using System.Net;
using System.Threading.Tasks;

namespace Etherna.Scrinium.AspNetCore.UI.Auth.Filters
{
    /// <summary>
    /// Authorize only requests coming directly from the host running the application.
    /// The check is based on the connection addresses, so it is meaningful only when clients reach
    /// the application without intermediaries: a request carrying forwarding headers traversed a
    /// proxy, whose address says nothing about the client, and is always denied. Deployments behind
    /// a reverse proxy must replace this filter with one validating an authenticated principal.
    /// </summary>
    public class LocalRequestsOnlyAuthFilter : IDashboardAuthFilter
    {
        // Fields.
        private static readonly string[] forwardingHeaderNames =
        [
            "Forwarded",
            "X-Forwarded-For",
            "X-Forwarded-Host",
            "X-Forwarded-Prefix",
            "X-Forwarded-Proto",
            "X-Original-For",
            "X-Original-Host",
            "X-Original-Prefix",
            "X-Original-Proto",
            "X-Real-IP"
        ];

        // Methods.
        public Task<bool> AuthorizeAsync(HttpContext? context)
        {
            if (context is null)
                return Task.FromResult(false);

            /* A forwarded request comes from a proxy: the connection remote address is the proxy
             * one, not the client one, so an address based check can't authorize it. Deny on any
             * forwarding evidence: the headers added by proxies, and the ones the forwarded headers
             * middleware moves the original values to — failing closed also when the middleware
             * rewrote the connection with a header supplied value. */
            foreach (var headerName in forwardingHeaderNames)
                if (context.Request.Headers.ContainsKey(headerName))
                    return Task.FromResult(false);

            var remoteIpAddress = context.Connection.RemoteIpAddress;

            // If unknown, assume not local.
            if (remoteIpAddress is null)
                return Task.FromResult(false);

            // Check if loopback, IPv4 mapped form included.
            if (IPAddress.IsLoopback(remoteIpAddress))
                return Task.FromResult(true);

            // Compare with local address.
            if (remoteIpAddress.Equals(context.Connection.LocalIpAddress))
                return Task.FromResult(true);

            return Task.FromResult(false);
        }
    }
}
