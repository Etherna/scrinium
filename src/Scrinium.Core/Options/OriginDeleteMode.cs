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

namespace Etherna.Scrinium.Core.Options
{
    /// <summary>
    /// How the documents referencing a model react when the model is deleted through its
    /// repository, propagated in background by the deletion propagation task. Declared per
    /// reference on
    /// <see cref="Serialization.Serializers.ReferenceSerializerConfiguration.OriginDelete"/>.
    /// Only domain deletes propagate: raw bulk deletes, deletes by other applications, and
    /// referencing documents of another db context stay out.
    /// Values are ordered by invasiveness on the referencing documents.
    /// </summary>
    public enum OriginDeleteMode
    {
        /// <summary>
        /// Keep the reference dangling: loads of its summary react per
        /// <see cref="Serialization.Serializers.ReferenceSerializerConfiguration.MissingOriginDocument"/>.
        /// </summary>
        KeepReference = 0,

        /// <summary>
        /// Remove the reference from the referencing documents: a reference hosted as an
        /// array item is pulled out of its array, any other one is set to null. The default:
        /// a domain delete keeps the references consistent on its own.
        /// </summary>
        RemoveReference = 1,

        /// <summary>
        /// Delete the referencing documents, with a domain delete that propagates their own
        /// reference policies in turn.
        /// </summary>
        DeleteReferencingDocument = 2
    }
}
