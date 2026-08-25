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

using System.Linq;
using System.Reflection;

namespace Etherna.MongODM.Core.Extensions
{
    public static class MemberInfoExtensions
    {
        public static bool IsSameAs(this MemberInfo memberInfo, MemberInfo otherMemberInfo)
        {
            if (memberInfo == null)
                return otherMemberInfo == null;
            if (otherMemberInfo == null)
                return false;
            if (Equals(memberInfo, otherMemberInfo))
                return true;
            if (memberInfo.Name != otherMemberInfo.Name)
                return false;
            
            var memberInfoDeclaringType = memberInfo.DeclaringType!;
            var otherMemberInfoDeclaringType = otherMemberInfo.DeclaringType!;
            return memberInfoDeclaringType == otherMemberInfoDeclaringType ||
                   memberInfoDeclaringType.GetTypeInfo().IsSubclassOf(otherMemberInfoDeclaringType) ||
                   otherMemberInfoDeclaringType.GetTypeInfo().IsSubclassOf(memberInfoDeclaringType) ||
                   memberInfoDeclaringType.GetTypeInfo().ImplementedInterfaces.Contains(otherMemberInfo.DeclaringType) ||
                   otherMemberInfoDeclaringType.GetTypeInfo().ImplementedInterfaces.Contains(memberInfo.DeclaringType);
        }
    }
}
