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

using System.Collections.Generic;

namespace Etherna.MongODM.Core.Extensions
{
    public static class ExtraElementsExtensions
    {
        public static TValue? TryGetExtraElementValue<TValue>(this IDictionary<string, object>? extraElements, string key) =>
            extraElements is not null &&
                extraElements.TryGetValue(key, out var objValue) &&
                objValue is TValue value ?
            value :
            default;
    }
}
