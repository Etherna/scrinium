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

using Etherna.MongODM.Core.Attributes;
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
        private List<MigrationLogBase> _logs = new();

        // Constructors.
        public DbMigrationOperation(IDbContext dbContext)
            : base(dbContext)
        {
            CurrentStatus = Status.New;
        }
        protected DbMigrationOperation() { }

        // Properties.
        public virtual DateTime? CompletedDateTime { get; protected set; }
        public virtual Status CurrentStatus { get; protected set; }
        public virtual IEnumerable<MigrationLogBase> Logs
        {
            get => _logs;
            protected set => _logs = new List<MigrationLogBase>(value ?? Array.Empty<MigrationLogBase>());
        }
        public virtual string? TaskId { get; protected set; }

        // Methods.
        [PropertyAlterer(nameof(Logs))]
        public virtual void AddLog(MigrationLogBase log)
        {
            ArgumentNullException.ThrowIfNull(log);

            _logs.Add(log);
        }

        [PropertyAlterer(nameof(CurrentStatus))]
        public virtual void TaskCancelled()
        {
            if (CurrentStatus == Status.Completed ||
                CurrentStatus == Status.Failed)
                throw new InvalidOperationException();

            CurrentStatus = Status.Cancelled;
        }

        [PropertyAlterer(nameof(CompletedDateTime))]
        [PropertyAlterer(nameof(CurrentStatus))]
        public virtual void TaskCompleted()
        {
            if (CurrentStatus != Status.Running)
                throw new InvalidOperationException();

            CompletedDateTime = DateTime.Now;
            CurrentStatus = Status.Completed;
        }

        [PropertyAlterer(nameof(CurrentStatus))]
        public virtual void TaskFailed()
        {
            if (CurrentStatus == Status.Completed ||
                CurrentStatus == Status.Cancelled)
                throw new InvalidOperationException();

            CurrentStatus = Status.Failed;
        }

        [PropertyAlterer(nameof(CurrentStatus))]
        [PropertyAlterer(nameof(TaskId))]
        public virtual void TaskStarted(string taskId)
        {
            ArgumentNullException.ThrowIfNull(taskId);

            if (CurrentStatus != Status.New)
                throw new InvalidOperationException();

            CurrentStatus = Status.Running;
            TaskId = taskId;
        }
    }
}
