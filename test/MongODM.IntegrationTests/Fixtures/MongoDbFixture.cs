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
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Etherna.MongODM.IntegrationTests.Fixtures
{
    /// <summary>
    /// Provides a MongoDB instance for integration tests: uses the url from the
    /// MONGODM_TEST_DB_URL environment variable when set (e.g. on CI with a service
    /// container), otherwise spawns a local throwaway mongod process.
    /// </summary>
    internal sealed class MongoDbFixture : IDisposable
    {
        // Consts.
        private const string DbUrlEnvVariable = "MONGODM_TEST_DB_URL";

        // Fields.
        private string? dataDirectory;
        private Process? mongodProcess;
        private static readonly TimeSpan startupTimeout = TimeSpan.FromSeconds(30);

        // Constructors and dispose.
        public MongoDbFixture()
        {
            var envUrl = Environment.GetEnvironmentVariable(DbUrlEnvVariable);
            if (!string.IsNullOrEmpty(envUrl))
            {
                DbUrl = envUrl.TrimEnd('/');
                return;
            }

            DbUrl = StartLocalMongod();
        }

        public void Dispose()
        {
            if (mongodProcess is not null)
            {
                try
                {
                    if (!mongodProcess.HasExited)
                        mongodProcess.Kill(entireProcessTree: true);
                    mongodProcess.WaitForExit(5000);
                }
                catch (InvalidOperationException) { }
                mongodProcess.Dispose();
            }

            if (dataDirectory is not null)
            {
                try { Directory.Delete(dataDirectory, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        // Properties.
        public string DbUrl { get; }

        // Helpers.
        private static int GetFreeTcpPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        private string StartLocalMongod()
        {
            var port = GetFreeTcpPort();
            dataDirectory = Path.Combine(Path.GetTempPath(), "mongodm-it-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataDirectory);

            try
            {
                //log to file: redirecting standard streams without reading them
                //would block mongod when the pipe buffer fills up
                var logPath = Path.Combine(dataDirectory, "mongod.log");
                mongodProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "mongod",
                    Arguments = $"--dbpath \"{dataDirectory}\" --port {port} --bind_ip 127.0.0.1 --logpath \"{logPath}\"",
                    UseShellExecute = false
                });
            }
            catch (Win32Exception e)
            {
                throw new InvalidOperationException(
                    $"Can't run integration tests: mongod binary not found and {DbUrlEnvVariable} is not set", e);
            }

            if (mongodProcess is null)
                throw new InvalidOperationException("Can't start mongod process");

            WaitForTcpPort(port);

            return $"mongodb://127.0.0.1:{port}";
        }

        private void WaitForTcpPort(int port)
        {
            var timeoutLimit = DateTime.UtcNow + startupTimeout;
            while (DateTime.UtcNow < timeoutLimit)
            {
                if (mongodProcess!.HasExited)
                    throw new InvalidOperationException($"mongod exited prematurely with code {mongodProcess.ExitCode}");

                try
                {
                    using var client = new TcpClient();
                    client.Connect(IPAddress.Loopback, port);
                    return;
                }
                catch (SocketException)
                {
                    Thread.Sleep(200);
                }
            }
            throw new InvalidOperationException($"mongod didn't become ready within {startupTimeout.TotalSeconds}s");
        }
    }
}
