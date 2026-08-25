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

using Etherna.MongoDB.Bson.Serialization;
using Etherna.MongoDB.Bson.Serialization.Options;
using Etherna.MongoDB.Bson.Serialization.Serializers;
using Etherna.Scrinium.Core.Serialization.Mapping;
using System;
using System.Collections.Generic;

namespace Etherna.Scrinium.Core.Serialization.Serializers
{
    public class ReadOnlyDictionarySerializer<TKey, TValue> :
        DictionarySerializerBase<IReadOnlyDictionary<TKey, TValue>, TKey, TValue>,
        IChildSerializerConfigurable,
        IDictionaryRepresentationConfigurable<ReadOnlyDictionarySerializer<TKey, TValue>>,
        IModelMapsHandlingSerializer
        where TKey : notnull
    {
        // Constructors.
        public ReadOnlyDictionarySerializer()
        { }

        public ReadOnlyDictionarySerializer(DictionaryRepresentation dictionaryRepresentation)
            : base(dictionaryRepresentation)
        { }

        public ReadOnlyDictionarySerializer(DictionaryRepresentation dictionaryRepresentation, IBsonSerializer<TKey> keySerializer, IBsonSerializer<TValue> valueSerializer)
            : base(dictionaryRepresentation, keySerializer, valueSerializer)
        { }

        // Properties.
        public IBsonSerializer ChildSerializer => ValueSerializer;

        public IEnumerable<IModelMap> HandledModelMaps =>
            (ValueSerializer as IModelMapsHandlingSerializer)?.HandledModelMaps ??
            Array.Empty<IModelMap>();

        // Public methods.
        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, IReadOnlyDictionary<TKey, TValue> value)
        {
            ArgumentNullException.ThrowIfNull(value);

            // Force to exclude enumerable actual type from serialization.
            args = new BsonSerializationArgs(value.GetType(), true, args.SerializeIdFirst);

            base.Serialize(context, args, value);
        }

        public IBsonSerializer WithChildSerializer(IBsonSerializer childSerializer) =>
            WithValueSerializer((IBsonSerializer<TValue>)childSerializer);

        /// <summary>
        /// Returns a serializer that has been reconfigured with the specified dictionary representation.
        /// </summary>
        /// <param name="dictionaryRepresentation">The dictionary representation.</param>
        /// <returns>The reconfigured serializer.</returns>
        public ReadOnlyDictionarySerializer<TKey, TValue> WithDictionaryRepresentation(DictionaryRepresentation dictionaryRepresentation) =>
            dictionaryRepresentation == DictionaryRepresentation ?
                this :
                new ReadOnlyDictionarySerializer<TKey, TValue>(dictionaryRepresentation, KeySerializer, ValueSerializer);

        /// <summary>
        /// Returns a serializer that has been reconfigured with the specified dictionary representation and key value serializers.
        /// </summary>
        /// <param name="dictionaryRepresentation">The dictionary representation.</param>
        /// <param name="keySerializer">The key serializer.</param>
        /// <param name="valueSerializer">The value serializer.</param>
        /// <returns>The reconfigured serializer.</returns>
        public ReadOnlyDictionarySerializer<TKey, TValue> WithDictionaryRepresentation(
            DictionaryRepresentation dictionaryRepresentation,
            IBsonSerializer<TKey> keySerializer,
            IBsonSerializer<TValue> valueSerializer) =>
            dictionaryRepresentation == DictionaryRepresentation && keySerializer == KeySerializer && valueSerializer == ValueSerializer ?
                this :
                new ReadOnlyDictionarySerializer<TKey, TValue>(dictionaryRepresentation, keySerializer, valueSerializer);

        /// <summary>
        /// Returns a serializer that has been reconfigured with the specified key serializer.
        /// </summary>
        /// <param name="keySerializer">The key serializer.</param>
        /// <returns>The reconfigured serializer.</returns>
        public ReadOnlyDictionarySerializer<TKey, TValue> WithKeySerializer(IBsonSerializer<TKey> keySerializer) =>
            keySerializer == KeySerializer ?
                this :
                new ReadOnlyDictionarySerializer<TKey, TValue>(DictionaryRepresentation, keySerializer, ValueSerializer);

        /// <summary>
        /// Returns a serializer that has been reconfigured with the specified value serializer.
        /// </summary>
        /// <param name="valueSerializer">The value serializer.</param>
        /// <returns>The reconfigured serializer.</returns>
        public ReadOnlyDictionarySerializer<TKey, TValue> WithValueSerializer(IBsonSerializer<TValue> valueSerializer) =>
            valueSerializer == ValueSerializer ?
                this :
                new ReadOnlyDictionarySerializer<TKey, TValue>(DictionaryRepresentation, KeySerializer, valueSerializer);

        // Protected methods.
        protected override ICollection<KeyValuePair<TKey, TValue>> CreateAccumulator() =>
            new Dictionary<TKey, TValue>();
        
        // Explicit interface implementations.
        IBsonSerializer IDictionaryRepresentationConfigurable.WithDictionaryRepresentation(DictionaryRepresentation dictionaryRepresentation) =>
            WithDictionaryRepresentation(dictionaryRepresentation);
    }
}
