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

using Etherna.MongODM.Core.Models;
using System.Collections.Generic;
using System.Linq;

namespace Etherna.MongODM.Core.Comparers
{
    public class FakeModelComparer : EqualityComparer<FakeModel?>
    {
        public override bool Equals(FakeModel? x, FakeModel? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;

            //(x.EnumerableProp is null) XOR (y.EnumerableProp is null)
            if ((x.EnumerableProp == null || y.EnumerableProp == null) &&
                !(x.EnumerableProp == null && y.EnumerableProp == null))
                return false;
            //because prev XOR is false, if (x.EnumerableProp != null) so (y.EnumerableProp != null)
            if (x.EnumerableProp != null && y.EnumerableProp != null)
            {
                if (x.EnumerableProp.Count() != y.EnumerableProp.Count())
                    return false;
                if (x.EnumerableProp.Zip(y.EnumerableProp, (xVal, yVal) => new { xVal, yVal })
                    .Any(pair => !Equals(pair.xVal, pair.yVal)))
                    return false;
            }

            //XOR
            if ((x.ExtraElements == null || y.ExtraElements == null) &&
                !(x.ExtraElements == null && y.ExtraElements == null))
                return false;
            //as before
            if (x.ExtraElements != null && y.ExtraElements != null)
            {
                if (x.ExtraElements.Count != y.ExtraElements.Count)
                    return false;
                if (x.ExtraElements.Zip(y.ExtraElements, (xVal, yVal) => new { xVal, yVal })
                    .Any(pair => !Equals(pair.xVal, pair.yVal)))
                    return false;
            }

            if (x.Id != y.Id)
                return false;

            if (x.IntegerProp != y.IntegerProp)
                return false;

            if (!Equals(x.ObjectProp, y.ObjectProp))
                return false;

            if (x.StringProp != y.StringProp)
                return false;

            return true;
        }

        public override int GetHashCode(FakeModel? obj)
        {
            if (obj is null) return -1;
            return obj.GetHashCode();
        }
    }
}
