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
using Etherna.MongODM.Core.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Etherna.MongODM.Core.ProxyModels
{
    public class AuditableInterceptor<TModel> : ModelInterceptorBase<TModel>
    {
        // Fields.
        private bool isAuditingEnabled;
        private readonly HashSet<MemberInfo> changedMembers = new();

        // Constructors.
        public AuditableInterceptor(IEnumerable<Type> additionalInterfaces)
            : base(additionalInterfaces)
        { }

        // Protected methods.
        protected override bool InterceptInterface(IInvocation invocation)
        {
            ArgumentNullException.ThrowIfNull(invocation);

            // Intercept ISummarizable invocations
            if (invocation.Method.DeclaringType == typeof(IAuditable))
            {
                if (invocation.Method.Name == $"get_{nameof(IAuditable.IsAuditingEnabled)}")
                    invocation.ReturnValue = isAuditingEnabled;
                else if (invocation.Method.Name == $"get_{nameof(IAuditable.IsChanged)}")
                    invocation.ReturnValue = changedMembers.Count != 0;
                else if (invocation.Method.Name == $"get_{nameof(IAuditable.ChangedMembers)}")
                    invocation.ReturnValue = changedMembers;
                else if (invocation.Method.Name == nameof(IAuditable.DisableAuditing))
                    isAuditingEnabled = false;
                else if (invocation.Method.Name == nameof(IAuditable.EnableAuditing))
                    isAuditingEnabled = true;
                else if (invocation.Method.Name == nameof(IAuditable.ResetChangedMembers))
                    changedMembers.Clear();
                else
                    throw new NotSupportedException();

                return true;
            }

            return false;
        }

        protected override void InterceptModel(IInvocation invocation)
        {
            ArgumentNullException.ThrowIfNull(invocation);

            // Filter sets.
            if (isAuditingEnabled)
            {
                if (invocation.Method.Name.StartsWith("set_", StringComparison.InvariantCulture))
                {
                    var propertyName = invocation.Method.Name.Substring(4);
                    var propertyInfo = typeof(TModel).GetMember(propertyName).Single();

                    // Add property to edited set.
                    changedMembers.Add(propertyInfo);
                }
                else if (invocation.Method.Name.StartsWith("get_", StringComparison.InvariantCulture))
                {
                    //ignore get
                }
                else //normal methods
                {
                    var alteredPropertiesName = from attribute in invocation.Method.GetCustomAttributes<PropertyAltererAttribute>(true)
                                                select attribute.PropertyName;
                    var propertiesInfo = from propertyName in alteredPropertiesName
                                         select typeof(TModel).GetMember(propertyName).Single();

                    // Add properties to edited set.
                    foreach (var propertyInfo in propertiesInfo)
                        changedMembers.Add(propertyInfo);
                }
            }

            invocation.Proceed();
        }
    }
}
