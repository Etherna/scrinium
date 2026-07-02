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
using Etherna.MongODM.Core.Domain.Models;
using Etherna.MongODM.Core.Extensions;
using Etherna.MongODM.Core.Repositories;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Etherna.MongODM.Core.ProxyModels
{
    public class ReferenceableInterceptor<TModel, TKey> : ModelInterceptorBase<TModel>
        where TModel : class, IEntityModel<TKey>
    {
        // Fields.
        private readonly ILogger<ReferenceableInterceptor<TModel, TKey>> logger;
        private readonly IRepository repository;
        private readonly Dictionary<string, bool> settedMemberNames = new(); //<memberName, isFromSummary>

        private bool isSummary;

        // Constructors.
        public ReferenceableInterceptor(
            IEnumerable<Type> additionalInterfaces,
            IDbContext dbContext,
            ILogger<ReferenceableInterceptor<TModel, TKey>> logger)
            : base(additionalInterfaces)
        {
            ArgumentNullException.ThrowIfNull(dbContext);

            repository = dbContext.RepositoryRegistry.GetRepositoryByHandledModelType(typeof(TModel));
            this.logger = logger;
        }

        // Protected methods.
        protected override bool InterceptInterface(IInvocation invocation)
        {
            ArgumentNullException.ThrowIfNull(invocation);

            // Intercept ISummarizable invocations
            if (invocation.Method.DeclaringType == typeof(IReferenceable))
            {
                if (invocation.Method.Name == $"get_{nameof(IReferenceable.IsSummary)}")
                {
                    invocation.ReturnValue = isSummary;
                }
                else if (invocation.Method.Name == $"get_{nameof(IReferenceable.SettedMemberNames)}")
                {
                    invocation.ReturnValue = settedMemberNames.Select(pair => pair.Key);
                }
                else if (invocation.Method.Name == nameof(IReferenceable.ClearSettedMembers))
                {
                    settedMemberNames.Clear();
                }
                else if (invocation.Method.Name == nameof(IReferenceable.MergeFullModel))
                {
                    MergeFullModel((TModel)invocation.Proxy, invocation.GetArgumentValue(0) as TModel);
                }
                else if (invocation.Method.Name == nameof(IReferenceable.SetAsSummary))
                {
                    isSummary = true;

#pragma warning disable CS8604 // Possible null reference argument.
                    var summaryLoadedMemberNames = ((IEnumerable<string>)invocation.GetArgumentValue(0)!).ToArray();
#pragma warning restore CS8604 // Possible null reference argument.
                    foreach (var memberName in summaryLoadedMemberNames)
                        settedMemberNames[memberName] = true;
                }
                else
                {
                    throw new NotSupportedException();
                }
                return true;
            }

            return false;
        }

        protected override void InterceptModel(IInvocation invocation)
        {
            ArgumentNullException.ThrowIfNull(invocation);

            // Filter gets.
            if (invocation.Method.Name.StartsWith("get_", StringComparison.InvariantCulture) && isSummary)
            {
                var propertyName = invocation.Method.Name.Substring(4);

                // If member is not loaded, load the full object.
                if (!settedMemberNames.ContainsKey(propertyName))
                {
                    var task = FullLoadAsync((TModel)invocation.Proxy);
                    task.Wait();
                }
            }

            // Filter sets.
            else if (invocation.Method.Name.StartsWith("set_", StringComparison.InvariantCulture))
            {
                var propertyName = invocation.Method.Name.Substring(4);

                // Report property as setted.
                settedMemberNames[propertyName] = false;
            }

            // Filter normal methods.
            else
            {
                var attributes = invocation.Method.GetCustomAttributes<PropertyAltererAttribute>(true) ?? Array.Empty<PropertyAltererAttribute>();
                foreach (var propertyName in from attribute in attributes
                                             select attribute.PropertyName)
                {
                    if (isSummary)
                    {
                        // If member is not setted and is summary, load the full object.
                        if (!settedMemberNames.ContainsKey(propertyName))
                        {
                            var task = FullLoadAsync((TModel)invocation.Proxy);
                            task.Wait();

                            break;
                        }
                    }
                    else
                    {
                        // Report property as setted.
                        settedMemberNames[propertyName] = false;
                    }
                }
            }

            invocation.Proceed();
        }

        // Helpers.
        private async Task FullLoadAsync(TModel model)
        {
            if (model.Id is null)
                throw new InvalidOperationException("model or id can't be null");

            if (isSummary)
            {
                // Merge full object to current.
                var fullModel = (await repository.TryFindOneAsync(model.Id).ConfigureAwait(false)) as TModel;
                MergeFullModel(model, fullModel);

                logger.SummaryModelFullLoaded(typeof(TModel), model.Id.ToString()!);
            }
        }

        private void MergeFullModel(TModel model, TModel? fullModel)
        {
            if (fullModel != null)
            {
                // Temporary disable auditing.
                (model as IAuditable)?.DisableAuditing();

                // Copy from full object every member in list that is not already loaded
                foreach (var member in ReflectionHelper.GetWritableInstanceProperties(typeof(TModel))
                                       .Where(info => !settedMemberNames.ContainsKey(info.Name) || settedMemberNames[info.Name])
                                       .ToArray())
                {
                    var value = ReflectionHelper.GetValue(fullModel, member);
                    ReflectionHelper.SetValue(model, member, value);
                }

                // Reenable auditing.
                (model as IAuditable)?.EnableAuditing();
            }

            isSummary = false;
        }
    }
}
