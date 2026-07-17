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
using Moq;
using System;
using System.Linq.Expressions;

namespace Etherna.MongODM.Core.MockHelpers
{
    public static class InterceptorMockHelper
    {
        public static Mock<Castle.DynamicProxy.IInvocation> GetExternalMethodInvocationMock<TProxy, TDeclaring>(
            string methodName,
            object[]? arguments = null,
            TProxy? proxyModel = null)
            where TProxy : class
        {
            var invocation = GetInvocationMock<TProxy, TDeclaring>(proxyModel);

            invocation.Setup(i => i.Method.Name)
                .Returns(methodName);
            if(arguments != null)
            {
                for (int j = 0; j < arguments.Length; j++)
                {
                    invocation.Setup(i => i.GetArgumentValue(j))
                        .Returns(arguments[j]);
                }
            }

            return invocation;
        }

        public static Mock<Castle.DynamicProxy.IInvocation> GetExternalPropertyGetInvocationMock<TProxy, TDeclaring, TMember>(
            Expression<Func<TDeclaring, TMember>> memberLambda,
            TProxy? proxyModel = null,
            TMember? returnValue = default)
            where TProxy : class
        {
            var invocation = GetInvocationMock<TProxy, TDeclaring>(proxyModel);
            var memberInfo = ReflectionHelper.GetMemberInfoFromLambda(memberLambda);

            invocation.Setup(i => i.Method.GetCustomAttributes(typeof(PropertyAltererAttribute), true))
                .Returns(Array.Empty<PropertyAltererAttribute>());
            invocation.Setup(i => i.Method.Name)
                .Returns($"get_{memberInfo.Name}");
            invocation.Setup(i => i.Method.ReturnType)
                .Returns(typeof(TMember));
            invocation.SetupProperty(i => i.ReturnValue);
            invocation.Setup(i => i.Proceed())
                .Callback(() => invocation.Object.ReturnValue = returnValue);

            return invocation;
        }

        public static Mock<Castle.DynamicProxy.IInvocation> GetExternalPropertySetInvocationMock<TProxy, TDeclaring, TMember>(
            Expression<Func<TDeclaring, TMember>> memberLambda,
            TMember value,
            TProxy? proxyModel = null)
            where TProxy : class
        {
            var invocation = GetInvocationMock<TProxy, TDeclaring>(proxyModel);
            var memberInfo = ReflectionHelper.GetMemberInfoFromLambda(memberLambda);

            invocation.Setup(i => i.Arguments)
                .Returns(new object[] { value! });
            invocation.Setup(i => i.Method.Name)
                .Returns($"set_{memberInfo.Name}");
            invocation.Setup(i => i.Method.ReturnType)
                .Returns(typeof(TMember));
            invocation.Setup(i => i.GetArgumentValue(0))
                .Returns(value!);

            return invocation;
        }

        public static Mock<Castle.DynamicProxy.IInvocation> GetMethodInvocationMock<TProxy>(
            string methodName,
            object[]? arguments = null,
            TProxy? proxyModel = null)
            where TProxy : class
        {
            return GetExternalMethodInvocationMock<TProxy, TProxy>(methodName, arguments, proxyModel);
        }

        public static Mock<Castle.DynamicProxy.IInvocation> GetPropertyGetInvocationMock<TProxy, TMember>(
            Expression<Func<TProxy, TMember>> memberLambda,
            TProxy? proxyModel = null,
            TMember? returnValue = default)
            where TProxy : class
        {
            return GetExternalPropertyGetInvocationMock(memberLambda, proxyModel, returnValue!);
        }

        public static Mock<Castle.DynamicProxy.IInvocation> GetPropertySetInvocationMock<TProxy, TMember>(
            Expression<Func<TProxy, TMember>> memberLambda,
            TMember value,
            TProxy? proxyModel = null)
            where TProxy : class
        {
            return GetExternalPropertySetInvocationMock(memberLambda, value, proxyModel);
        }

        // Helpers.

        private static Mock<Castle.DynamicProxy.IInvocation> GetInvocationMock<TProxy, TDeclaring>(
            TProxy? proxyModel)
            where TProxy : class
        {
            var invocation = new Mock<Castle.DynamicProxy.IInvocation>();

            invocation.Setup(i => i.Proxy)
                .Returns(proxyModel ?? new Mock<TProxy>().Object);
            invocation.Setup(i => i.Method.DeclaringType)
                .Returns(typeof(TDeclaring));
            invocation.SetupProperty(i => i.ReturnValue);

            return invocation;
        }
    }
}
