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

namespace Etherna.MongODM.Core.Options
{
    public class ModelMapSchemaIdOptions
    {
        /// <summary>
        /// Name of the document element carrying the model map schema id.
        /// </summary>
        public string ElementName { get; set; } = "_s";

        /// <summary>
        /// Element names recognized reading documents written with a previous element name.
        /// Writes always use <see cref="ElementName"/>: a document read through a fallback
        /// name migrates to the current one with its next whole document write.
        /// </summary>
        public IEnumerable<string> ReadFallbackElementNames { get; set; } = ["_m"];
    }
}
