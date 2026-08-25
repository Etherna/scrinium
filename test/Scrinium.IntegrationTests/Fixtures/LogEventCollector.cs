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

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace Etherna.Scrinium.IntegrationTests.Fixtures
{
    /// <summary>
    /// A logger recording the events emitted by a db context, so that a test can observe a
    /// state it can't reach otherwise (e.g. a flow entered its wait loop).
    /// </summary>
    public sealed class LogEventCollector : ILogger
    {
        // Fields.
        private readonly ConcurrentQueue<EventId> events = new();

        // Methods.
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public void Clear() => events.Clear();

        public bool HasLogged(string eventName) =>
            events.Any(loggedEvent => loggedEvent.Name == eventName);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            events.Enqueue(eventId);
    }
}
