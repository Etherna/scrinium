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
using System.Threading;

namespace Etherna.MongODM.Core.Utility
{
    public abstract class FreezableConfig : IFreezableConfig, IDisposable
    {
        // Fields.
        private readonly ReaderWriterLockSlim configLock = new(LockRecursionPolicy.SupportsRecursion);
        private bool disposed;

        // Dispose.
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed) return;

            // Dispose managed resources.
            if (disposing)
                configLock.Dispose();

            disposed = true;
        }

        // Properties.
        public bool IsFrozen { get; private set; }

        // Methods.
        public void Freeze()
        {
            configLock.EnterReadLock();
            try
            {
                if (IsFrozen) return;
            }
            finally
            {
                configLock.ExitReadLock();
            }

            configLock.EnterWriteLock();
            try
            {
                if (!IsFrozen)
                {
                    // Execute action.
                    FreezeAction();

                    // Freeze.
                    IsFrozen = true;
                }
            }
            finally
            {
                configLock.ExitWriteLock();
            }
        }

        // Protected methods.
        protected void ExecuteConfigAction(Action configAction)
        {
            ArgumentNullException.ThrowIfNull(configAction);

            ExecuteConfigAction(() =>
            {
                configAction();
                return 0;
            });
        }

        protected TReturn ExecuteConfigAction<TReturn>(Func<TReturn> configAction)
        {
            ArgumentNullException.ThrowIfNull(configAction);

            configLock.EnterWriteLock();
            try
            {
                if (IsFrozen)
                    throw new InvalidOperationException("Configuration is frozen");

                return configAction();
            }
            finally
            {
                configLock.ExitWriteLock();
            }
        }

        protected virtual void FreezeAction() { }
    }
}
