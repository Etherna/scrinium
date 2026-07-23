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

using Castle.DynamicProxy;
using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.Utility;
using System;
using System.Collections.Generic;

namespace Etherna.MongODM.Core.ProxyModels
{
    public class ChangeTrackingInterceptor<TModel> : ModelInterceptorBase<TModel>
    {
        // Fields.
        private readonly IDbContext? dbContext;

        // Constructors.
        public ChangeTrackingInterceptor(
            IEnumerable<Type> additionalInterfaces,
            IDbContextEngine dbContextEngine)
            : base(additionalInterfaces)
        {
            ArgumentNullException.ThrowIfNull(dbContextEngine);

            /* Bind change tracking to the db context scope running the current operation, if
             * any. Models created outside of a scope stay unbound, and models deserialized with
             * the no cache serializer modifier stay unbound too, keeping read only massive scans
             * out of the unit of work. */
            if (!dbContextEngine.SerializerModifierAccessor.IsNoCacheEnabled)
                dbContext = DbExecutionContextHandler.TryGetCurrentDbContext(dbContextEngine.ExecutionContext);
        }

        // Protected methods.
        protected override void InterceptModel(IInvocation invocation)
        {
            ArgumentNullException.ThrowIfNull(invocation);

            /* Any set or domain method invocation marks the model as a change candidate on its
             * scope: the actual changed members are computed at save by diffing the model
             * serialization against the baseline captured at load. Gets don't mutate, so they
             * are ignored; the db context also ignores the mark until a baseline exists (skipping
             * the sets replayed while deserializing) and while merging loaded data into a model. */
            if (dbContext is not null &&
                invocation.Proxy is IEntityModel entityModel &&
                !invocation.Method.Name.StartsWith("get_", StringComparison.InvariantCulture))
            {
                dbContext.MarkChangeCandidate(entityModel);
            }

            invocation.Proceed();
        }
    }
}
