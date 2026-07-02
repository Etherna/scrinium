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
using System;
using System.Collections.Generic;
using System.Linq;

namespace Etherna.MongODM.Core.ProxyModels
{
    public abstract class ModelInterceptorBase<TModel> : IInterceptor
    {
        private readonly IEnumerable<Type> additionalInterfaces;

        protected ModelInterceptorBase(IEnumerable<Type> additionalInterfaces)
        {
            this.additionalInterfaces = additionalInterfaces;
        }

        public void Intercept(IInvocation invocation)
        {
            ArgumentNullException.ThrowIfNull(invocation);

            if (additionalInterfaces.Contains(invocation.Method.DeclaringType))
            {
                var handled = InterceptInterface(invocation);
                if (handled)
                    return;
                invocation.Proceed();
            }
            else
            {
                // Check model type.
                if (invocation.Method.DeclaringType != typeof(TModel) &&
                    !invocation.Method.DeclaringType!.IsAssignableFrom(typeof(TModel)))
                {
                    throw new InvalidOperationException();
                }

                InterceptModel(invocation);
            }
        }

        /// <summary>
        /// Intercept an extra interface
        /// </summary>
        /// <param name="invocation">Current invocation</param>
        /// <returns>Returns true if the call handled the invocation</returns>
        protected virtual bool InterceptInterface(IInvocation invocation)
        {
            return false;
        }

        /// <summary>
        /// Intercept a call to the model
        /// </summary>
        /// <param name="invocation">Current invocation</param>
        protected virtual void InterceptModel(IInvocation invocation)
        {
            ArgumentNullException.ThrowIfNull(invocation);

            invocation.Proceed();
        }
    }
}
