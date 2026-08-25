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

namespace Etherna.Scrinium.Core.Utility
{
    public abstract class FreezableConfig : IFreezableConfig
    {
        // Fields.
        private readonly object configLock = new();
        private volatile bool _isFrozen;

        // Properties.
        public bool IsFrozen => _isFrozen;

        // Methods.
        /* Mutations and the freeze happen at engine build; after that, the frozen fast
         * path is the only concurrent access, avoiding any lock on serialization paths.
         * Monitor recursion keeps supporting config actions invoked by freeze actions. */
        public void Freeze()
        {
            if (_isFrozen) return;

            lock (configLock)
            {
                if (!_isFrozen)
                {
                    // Execute action.
                    FreezeAction();

                    // Freeze.
                    _isFrozen = true;
                }
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

            lock (configLock)
            {
                if (IsFrozen)
                    throw new InvalidOperationException("Configuration is frozen");

                return configAction();
            }
        }

        protected virtual void FreezeAction() { }
    }
}
