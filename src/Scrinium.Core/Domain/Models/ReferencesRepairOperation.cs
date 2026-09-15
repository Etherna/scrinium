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

using Etherna.Scrinium.Core.Domain.Models.ReferencesRepairOpAgg;
using Etherna.Scrinium.Core.Options;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Etherna.Scrinium.Core.Domain.Models
{
    /// <summary>
    /// The repair of the references pointing to missing origin documents on one collection,
    /// run like a db migration is run: it claims the db context lock, executes in background
    /// and reports its progress, one reference element path at a time. It is not a migration —
    /// it changes no document shape — so it is an operation of its own.
    /// </summary>
    public class ReferencesRepairOperation : OperationBase, IRunnableOperation
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
        private List<ReferencesRepairPathState> _pathStates = [];

        // Constructors.
        /// <param name="dbContextEngine">The engine of the db context owning the collection</param>
        /// <param name="repositoryName">The repository whose collection is repaired</param>
        /// <param name="repairModesByElementPath">What the operation applies to each
        /// reference element path: its plan, which only the operation itself advances</param>
        /// <param name="isDryRun">If true, the repair runs with its collection writes simulated</param>
        public ReferencesRepairOperation(
            IDbContextEngine dbContextEngine,
            string repositoryName,
            IReadOnlyDictionary<string, OriginDeleteMode> repairModesByElementPath,
            bool isDryRun = false)
            : base(dbContextEngine)
        {
            ArgumentNullException.ThrowIfNull(repairModesByElementPath);

            CurrentStatus = Status.New;
            IsDryRun = isDryRun;
            //ordered by path, so the plan reads the same wherever it is rendered
            PathStates = repairModesByElementPath
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new ReferencesRepairPathState(pair.Key, pair.Value));
            RepositoryName = repositoryName;
        }
        protected ReferencesRepairOperation() { }

        // Properties.
        public virtual DateTimeOffset? CompletedDateTime { get; protected set; }
        public virtual Status CurrentStatus { get; protected set; }
        public virtual bool IsDryRun { get; protected set; }
        /// <summary>
        /// True while the operation is neither closed nor cancelled: a status reporting a
        /// repair in progress, whether or not its owner is still alive.
        /// </summary>
        public virtual bool IsOpen => CurrentStatus is Status.New or Status.Running;

        /// <summary>
        /// What the operation does to each reference element path it was started with, and how
        /// far it got: its plan, readable before anything runs.
        /// </summary>
        public virtual IEnumerable<ReferencesRepairPathState> PathStates
        {
            get => _pathStates;
            protected set => _pathStates = [.. value ?? []];
        }

        /// <summary>
        /// The repository whose collection this operation repairs.
        /// </summary>
        public virtual string RepositoryName { get; protected set; } = null!;

        public virtual string? TaskId { get; protected set; }

        // Methods.
        /// <summary>
        /// Close every path the operation hasn't finished with the error that stopped it: a
        /// repair fails whole, so what didn't complete records why.
        /// </summary>
        public virtual void FailOpenPaths(string errorMessage)
        {
            for (var i = 0; i < _pathStates.Count; i++)
                if (_pathStates[i].State is ReferencesRepairPathState.ExecutionState.Pending
                                          or ReferencesRepairPathState.ExecutionState.Executing)
                    _pathStates[i] = new ReferencesRepairPathState(
                        _pathStates[i].ElementPath,
                        _pathStates[i].RepairMode,
                        ReferencesRepairPathState.ExecutionState.Failed,
                        _pathStates[i].MissingOriginIdsCount,
                        _pathStates[i].UpdatedDocumentsCount,
                        _pathStates[i].DeletedDocumentsCount,
                        errorMessage);
        }

        /// <summary>
        /// Report a path as done, with what it repaired.
        /// </summary>
        public virtual void ReportPathEnded(
            string elementPath,
            long missingOriginIdsCount,
            long updatedDocumentsCount,
            long deletedDocumentsCount) =>
            ReplacePathState(
                elementPath,
                ReferencesRepairPathState.ExecutionState.Succeded,
                missingOriginIdsCount,
                updatedDocumentsCount,
                deletedDocumentsCount);

        /// <summary>
        /// Report what a path brought so far, while it is still running: the dashboard renders
        /// the counters growing, and an interrupted operation keeps what it reported.
        /// </summary>
        public virtual void ReportPathProgress(
            string elementPath,
            long missingOriginIdsCount,
            long updatedDocumentsCount,
            long deletedDocumentsCount) =>
            ReplacePathState(
                elementPath,
                ReferencesRepairPathState.ExecutionState.Executing,
                missingOriginIdsCount,
                updatedDocumentsCount,
                deletedDocumentsCount);

        /// <summary>
        /// Report a path its mode keeps: it is not repaired, and not even scanned.
        /// </summary>
        public virtual void ReportPathSkipped(string elementPath) =>
            ReplacePathState(elementPath, ReferencesRepairPathState.ExecutionState.Skipped, 0, 0, 0);

        /// <summary>
        /// Report every planned path as running, when the repair starts on the collection.
        /// </summary>
        public virtual void ReportPathsStarted()
        {
            for (var i = 0; i < _pathStates.Count; i++)
                _pathStates[i] = new ReferencesRepairPathState(
                    _pathStates[i].ElementPath,
                    _pathStates[i].RepairMode,
                    ReferencesRepairPathState.ExecutionState.Executing,
                    _pathStates[i].MissingOriginIdsCount,
                    _pathStates[i].UpdatedDocumentsCount,
                    _pathStates[i].DeletedDocumentsCount,
                    _pathStates[i].ErrorMessage);
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

        // Helpers.
        /* A path state is written once: advancing one replaces it in place, keeping the order
         * the operation was started with — the plan reads the same, whatever stage it is at. */
        private void ReplacePathState(
            string elementPath,
            ReferencesRepairPathState.ExecutionState state,
            long missingOriginIdsCount,
            long updatedDocumentsCount,
            long deletedDocumentsCount)
        {
            var index = _pathStates.FindIndex(pathState => pathState.ElementPath == elementPath);
            if (index < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(elementPath),
                    $"The operation carries no path \"{elementPath}\"");

            _pathStates[index] = new ReferencesRepairPathState(
                elementPath,
                _pathStates[index].RepairMode,
                state,
                missingOriginIdsCount,
                updatedDocumentsCount,
                deletedDocumentsCount,
                _pathStates[index].ErrorMessage);
        }
    }
}
