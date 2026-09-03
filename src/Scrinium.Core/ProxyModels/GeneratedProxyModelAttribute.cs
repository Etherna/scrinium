// Copyright 2020-present Etherna SA
// This file is part of Scrinium.
//
// Scrinium is free software: you can redistribute it and/or modify it under the terms of the
// GNU Lesser General Public License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
//
// Scrinium is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY;
// without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License along with Scrinium.
// If not, see <https://www.gnu.org/licenses/>.

using System;

namespace Etherna.Scrinium.Core.ProxyModels
{
    /// <summary>
    /// Declares at assembly level the proxy model type generated for a model type.
    /// Emitted by the proxy models source generator: the framework reads it from the
    /// model type's assembly to resolve its proxy type, with no reflection scans.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class GeneratedProxyModelAttribute(Type modelType, Type proxyModelType) : Attribute
    {
        // Properties.
        public Type ModelType { get; } = modelType;
        public Type ProxyModelType { get; } = proxyModelType;
    }
}
