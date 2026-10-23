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

using Etherna.Scrinium.Core.Domain.Models.DbMigrationOpAgg;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Etherna.Scrinium.Core.Domain.Models
{
    public class DbMigrationOperation : OperationBase, IRunnableOperation
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
        public DbMigrationOperation(
            IDbContextEngine dbContextEngine,
            bool isDryRun = false,
            bool isStopAtFirstErrorEnabled = false,
            bool isDeprecatedSchemaRewriteEnabled = false)
            : base(dbContextEngine)
        {
            CurrentStatus = Status.New;
            IsDeprecatedSchemaRewriteEnabled = isDeprecatedSchemaRewriteEnabled;
            IsDryRun = isDryRun;
            IsStopAtFirstErrorEnabled = isStopAtFirstErrorEnabled;
        }
        protected DbMigrationOperation() { }

        // Properties.
        /// <summary>
        /// The instant an abort of the operation was requested, when it was: the execution
        /// observes the stored request and stops at its next safe point, closing the operation
        /// cancelled. Null when nobody asked for it.
        /// </summary>
        public virtual DateTimeOffset? AbortRequestedDateTime { get; protected set; }
        /// <summary>
        /// The instant the operation closed, whatever its terminal status: completed, failed
        /// or cancelled. Null while it is open, and on the failed and cancelled operations
        /// closed by a version stamping it on the completed ones only.
        /// </summary>
        public virtual DateTimeOffset? CompletedDateTime { get; protected set; }
        public virtual Status CurrentStatus { get; protected set; }
        /// <summary>
        /// Why the operation failed, when it did: the exception aborting a documents scan or
        /// an index step, the unhandled exception of the execution, the lost lock lease, or
        /// the orphan sweep of a later start. Null on every other status.
        /// </summary>
        public virtual string? FailureReason { get; protected set; }
        /// <summary>
        /// If true, this operation also rewrites the documents left on a deprecated schema:
        /// the ones whose stored schema id isn't the active one of their concrete type, the
        /// ones carrying it under the deprecated element name included. It runs after the
        /// document migrations the application declares, inside the same operation, so both
        /// share the db context lock and the index steps around them.
        /// </summary>
        public virtual bool IsDeprecatedSchemaRewriteEnabled { get; protected set; }
        public virtual bool IsDryRun { get; protected set; }
        /// <summary>
        /// True while the operation is neither closed nor cancelled: a status reporting a
        /// migration in progress, whether or not its owner is still alive.
        /// </summary>
        public virtual bool IsOpen => CurrentStatus is Status.New or Status.Running;
        /// <summary>
        /// True while the operation is started and not closed: its task is executing it, or
        /// died while executing it.
        /// </summary>
        public virtual bool IsRunning => CurrentStatus == Status.Running;
        /// <summary>
        /// If true, a documents migration of this operation aborts at its first failing document,
        /// instead of skipping it and processing every other document.
        /// </summary>
        public virtual bool IsStopAtFirstErrorEnabled { get; protected set; }
        public virtual IEnumerable<MigrationLogBase> Logs
        {
            get => _logs;
            protected set => _logs = [..value ?? []];
        }
        public virtual string? TaskId { get; protected set; }

        // Methods.
        /// <summary>
        /// Add a documents migration log, removing the previous executing progress log of the
        /// same collection, if any: progress reports through one rolling entry per collection
        /// with its running counter, replaced at the end by the ended log, so the operation
        /// document stays bounded regardless of the collection size.
        /// </summary>
        /// <param name="log">The documents migration log to add</param>
        public virtual void AddDocumentMigrationLog(DocumentMigrationLog log)
        {
            ArgumentNullException.ThrowIfNull(log);

            _logs.RemoveAll(l =>
                l is DocumentMigrationLog { State: MigrationLogBase.ExecutionState.Executing } executingLog &&
                executingLog.CollectionName == log.CollectionName);
            _logs.Add(log);
        }

        public virtual void AddLog(MigrationLogBase log)
        {
            ArgumentNullException.ThrowIfNull(log);

            _logs.Add(log);
        }

        public virtual void ObserveAbortRequest(DateTimeOffset requestedDateTime)
        {
            if (!IsOpen)
                throw new InvalidOperationException();

            AbortRequestedDateTime ??= requestedDateTime;
        }

        /// <summary>
        /// Replace the index logs written by MongODM 0.24 and earlier, whose single index step
        /// built the new indexes and removed the old ones in one call, with the logs of the
        /// two steps it did: a build and a deletion log of the same repository, state and
        /// instant each. The operation then reads and rewrites as the current ones do, naming
        /// no obsolete log type.
        /// </summary>
        internal void ReplaceLegacyIndexLogs()
        {
#pragma warning disable CS0618 // Type or member is obsolete: the logs written by MongODM 0.24 are converted here
            for (var i = _logs.Count - 1; i >= 0; i--)
            {
                if (_logs[i] is not IndexMigrationLog legacyLog)
                    continue;

                _logs.RemoveAt(i);
                _logs.InsertRange(i,
                [
                    new BuildNewIndexesMigrationLog(legacyLog.Repository, legacyLog.State, legacyLog.CreationDateTime),
                    new DeleteOldIndexesMigrationLog(legacyLog.Repository, legacyLog.State, legacyLog.CreationDateTime)
                ]);
            }
#pragma warning restore CS0618 // Type or member is obsolete
        }

        public virtual void TaskCancelled()
        {
            if (CurrentStatus is Status.Completed or Status.Failed)
                throw DeniedTransition("cancel");

            CompletedDateTime = DateTimeOffset.UtcNow;
            CurrentStatus = Status.Cancelled;
            InterruptExecutingLogs();
        }

        public virtual void TaskCompleted()
        {
            if (CurrentStatus != Status.Running)
                throw DeniedTransition("complete");

            CompletedDateTime = DateTimeOffset.UtcNow;
            CurrentStatus = Status.Completed;
        }

        public virtual void TaskFailed(string reason)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            if (CurrentStatus is Status.Completed or Status.Cancelled)
                throw DeniedTransition("fail");

            CompletedDateTime = DateTimeOffset.UtcNow;
            CurrentStatus = Status.Failed;
            FailureReason = reason;
            InterruptExecutingLogs();
        }

        public virtual void TaskStarted(string? taskId = null)
        {
            if (CurrentStatus != Status.New)
                throw DeniedTransition("start");

            CurrentStatus = Status.Running;
            TaskId = taskId;
        }

        // Helpers.
        //a denied transition names the operation and the status denying it: it is what the task runner reports
        private InvalidOperationException DeniedTransition(string transition) =>
            new($"Can't {transition} the migration operation {Id}: its status is {CurrentStatus}");

        /* An operation closing while a step executes can't leave its log saying so: the log
         * closes interrupted, keeping its collection and counters, so the history tells which
         * step was running, and how far it got, when the operation stopped. */
        private void InterruptExecutingLogs()
        {
            foreach (var log in _logs.Where(log => log.State == MigrationLogBase.ExecutionState.Executing))
                log.Interrupt();
        }
    }
}
