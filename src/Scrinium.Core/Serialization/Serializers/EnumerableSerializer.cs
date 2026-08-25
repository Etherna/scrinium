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

using Etherna.MongoDB.Bson.Serialization;
using Etherna.MongoDB.Bson.Serialization.Serializers;
using Etherna.MongODM.Core.Serialization.Mapping;
using System;
using System.Collections.Generic;

namespace Etherna.MongODM.Core.Serialization.Serializers
{
    public class EnumerableSerializer<TItem> :
        EnumerableSerializerBase<IEnumerable<TItem>, TItem>,
        IChildSerializerConfigurable,
        IModelMapsHandlingSerializer
    {
        // Constructors.
        /// <summary>
        /// Initializes a new instance of the <see cref="StackSerializer{TItem}"/> class.
        /// </summary>
        public EnumerableSerializer()
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="StackSerializer{TItem}"/> class.
        /// </summary>
        /// <param name="itemSerializer">The item serializer.</param>
        public EnumerableSerializer(IBsonSerializer<TItem> itemSerializer)
            : base(itemSerializer)
        { }

        // Properties.
        public IBsonSerializer ChildSerializer => ItemSerializer;

        public IEnumerable<IModelMap> HandledModelMaps =>
            (ItemSerializer as IModelMapsHandlingSerializer)?.HandledModelMaps ??
            Array.Empty<IModelMap>();

        // Public methods.
        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, IEnumerable<TItem> value)
        {
            ArgumentNullException.ThrowIfNull(value);

            // Force to exclude enumerable actual type from serialization.
            args = new BsonSerializationArgs(value.GetType(), true, args.SerializeIdFirst);

            base.Serialize(context, args, value);
        }

        public IBsonSerializer WithChildSerializer(IBsonSerializer childSerializer) =>
            WithItemSerializer((IBsonSerializer<TItem>)childSerializer);

        /// <summary>
        /// Returns a serializer that has been reconfigured with the specified item serializer.
        /// </summary>
        /// <param name="itemSerializer">The item serializer.</param>
        /// <returns>The reconfigured serializer.</returns>
        public EnumerableSerializer<TItem> WithItemSerializer(IBsonSerializer<TItem> itemSerializer)
        {
            return new EnumerableSerializer<TItem>(itemSerializer);
        }

        // Protected methods.
        protected override void AddItem(object accumulator, TItem item)
        {
            ArgumentNullException.ThrowIfNull(accumulator);

            ((List<TItem>)accumulator).Add(item);
        }

        protected override object CreateAccumulator() =>
            new List<TItem>();

        protected override IEnumerable<TItem> EnumerateItemsInSerializationOrder(IEnumerable<TItem> value) =>
            value;

        protected override IEnumerable<TItem> FinalizeResult(object accumulator) =>
            (IEnumerable<TItem>)accumulator;
    }
}
