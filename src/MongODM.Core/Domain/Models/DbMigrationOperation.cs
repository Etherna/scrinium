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

using Etherna.MongODM.Core.Domain.Models.DbMigrationOpAgg;
using System;
using System.Collections.Generic;

namespace Etherna.MongODM.Core.Domain.Models
{
    public class DbMigrationOperation : OperationBase
    {
        // Enums.
        public enum Status
        {
            New,
            Running,
            Completed,
            Failed,
            Cancelled
        }

        // Fields.
        private List<MigrationLogBase> _logs = [];

        // Constructors.
        public DbMigrationOperation(IDbContextEngine dbContextEngine, bool isDryRun = false)
            : base(dbContextEngine)
        {
            CurrentStatus = Status.New;
            IsDryRun = isDryRun;
        }
        protected DbMigrationOperation() { }

        // Properties.
        public virtual DateTimeOffset? CompletedDateTime { get; protected set; }
        public virtual Status CurrentStatus { get; protected set; }
        public virtual bool IsDryRun { get; protected set; }
        public virtual IEnumerable<MigrationLogBase> Logs
        {
            get => _logs;
            protected set => _logs = [..value ?? []];
        }
        public virtual string? TaskId { get; protected set; }

        // Methods.
        public virtual void AddLog(MigrationLogBase log)
        {
            ArgumentNullException.ThrowIfNull(log);

            _logs.Add(log);
        }

        public virtual void TaskCancelled()
        {
            if (CurrentStatus is Status.Completed or Status.Failed)
                throw new InvalidOperationException();

            CurrentStatus = Status.Cancelled;
        }

        public virtual void TaskCompleted()
        {
            if (CurrentStatus != Status.Running)
                throw new InvalidOperationException();

            CompletedDateTime = DateTimeOffset.UtcNow;
            CurrentStatus = Status.Completed;
        }

        public virtual void TaskFailed()
        {
            if (CurrentStatus is Status.Completed or Status.Cancelled)
                throw new InvalidOperationException();

            CurrentStatus = Status.Failed;
        }

        public virtual void TaskStarted(string? taskId = null)
        {
            if (CurrentStatus != Status.New)
                throw new InvalidOperationException();

            CurrentStatus = Status.Running;
            TaskId = taskId;
        }
    }
}
