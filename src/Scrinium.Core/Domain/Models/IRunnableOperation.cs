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

namespace Etherna.Scrinium.Core.Domain.Models
{
    /// <summary>
    /// A db operation executed in background under the db context lock: it claims the lock
    /// with its own id at the start, resumes the claim in its task, and closes completed,
    /// failed or cancelled. What the operation does between those transitions is its own;
    /// this is the lifecycle <see cref="Utility.DbOperationLifecycle"/> drives for every kind.
    /// </summary>
    public interface IRunnableOperation
    {
        /// <summary>
        /// The operation id, claiming the db context lock.
        /// </summary>
        string Id { get; }

        /// <summary>
        /// True while the operation is neither closed nor cancelled: a status that reports a
        /// work in progress, whether or not its owner is still alive.
        /// </summary>
        bool IsOpen { get; }

        void TaskCancelled();
        void TaskCompleted();
        void TaskFailed();
        void TaskStarted(string? taskId = null);
    }
}
