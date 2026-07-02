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

using Etherna.ExecContext;
using Etherna.MongoDB.Bson;
using Etherna.MongoDB.Bson.IO;
using Etherna.MongoDB.Bson.Serialization;
using Etherna.MongoDB.Bson.Serialization.Conventions;
using Etherna.MongoDB.Bson.Serialization.Serializers;
using Etherna.MongODM.Core.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Etherna.MongODM.Core.Conventions
{
    public class HierarchicalProxyTolerantDiscriminatorConvention : IHierarchicalDiscriminatorConvention
    {
        // Fields.
        private readonly IDbContext? _dbContext; //remove nullability with constructors that don't ask it, when will be possible
        private readonly IExecutionContext? executionContext;

        // Constructors.
        [SuppressMessage("Usage", "CA2249:Consider using \'string.Contains\' instead of \'string.IndexOf\'")]
        [SuppressMessage("Globalization", "CA1307:Specify StringComparison for clarity")]
        public HierarchicalProxyTolerantDiscriminatorConvention(
            IDbContext dbContext,
            string elementName)
        {
            _dbContext = dbContext;

            ElementName = elementName ?? throw new ArgumentNullException(nameof(elementName));
            if (elementName.IndexOf('\0') != -1)
                throw new ArgumentException("Element names cannot contain nulls.", nameof(elementName));
        }

        /// <summary>
        /// Only needed for static registration on <see cref="object"/>, used when dbcontext is not available.
        /// Remove when <see cref="BsonSerializer.LookupDiscriminatorConvention(Type)"/> static call will be removed.
        /// </summary>
        /// <param name="elementName">Discriminator element name</param>
        /// <param name="executionContext">Execution context</param>
        [SuppressMessage("Usage", "CA2249:Consider using \'string.Contains\' instead of \'string.IndexOf\'")]
        [SuppressMessage("Globalization", "CA1307:Specify StringComparison for clarity")]
        public HierarchicalProxyTolerantDiscriminatorConvention(
            string elementName,
            IExecutionContext executionContext)
        {
            ElementName = elementName ?? throw new ArgumentNullException(nameof(elementName));
            if (elementName.IndexOf('\0') != -1)
                throw new ArgumentException("Element names cannot contain nulls.", nameof(elementName));

            this.executionContext = executionContext;
        }

        public IDbContext DbContext
        {
            get
            {
                if (_dbContext is not null)
                    return _dbContext;

                /* If we didn't injected a dbContext, this is an instance retrieved from a static invoke.
                 * Try to find it from execution contenxt. */
                if (executionContext is null)
                    throw new InvalidOperationException();

                return DbExecutionContextHandler.TryGetCurrentDbContext(executionContext)
                    ?? throw new InvalidOperationException();
            }
        }
        public string ElementName { get; }

        // Methods.
        public Type GetActualType(IBsonReader bsonReader, Type nominalType)
        {
            ArgumentNullException.ThrowIfNull(bsonReader);

            //the BsonReader is sitting at the value whose actual type needs to be found
            var bsonType = bsonReader.GetCurrentBsonType();
            if (bsonType == BsonType.Document)
            {
                //we can skip looking for a discriminator if nominalType has no discriminated sub types
                if (DbContext.DiscriminatorRegistry.IsTypeDiscriminated(nominalType))
                {
                    var bookmark = bsonReader.GetBookmark();
                    bsonReader.ReadStartDocument();
                    var actualType = nominalType;
                    if (bsonReader.FindElement(ElementName))
                    {
                        var context = BsonDeserializationContext.CreateRoot(bsonReader);
                        var discriminator = BsonValueSerializer.Instance.Deserialize(context);
                        if (discriminator.IsBsonArray)
                        {
                            discriminator = discriminator.AsBsonArray.Last(); //last item is leaf class discriminator
                        }
                        actualType = DbContext.DiscriminatorRegistry.LookupActualType(nominalType, discriminator);
                    }
                    bsonReader.ReturnToBookmark(bookmark);
                    return actualType;
                }
            }

            return nominalType;
        }

        /// <summary>
        /// Gets the discriminator value for an actual type.
        /// </summary>
        /// <param name="nominalType">The nominal type.</param>
        /// <param name="actualType">The actual type.</param>
        /// <returns>The discriminator value.</returns>
        public BsonValue? GetDiscriminator(Type nominalType, Type actualType)
        {
            // Remove proxy type.
            actualType = DbContext.ProxyGenerator.PurgeProxyType(actualType);
            
            // Find active schema for model type.
            if (!DbContext.MapRegistry.TryGetModelMap(actualType, out var modelMap))
                return null;
            var schema = modelMap.ActiveSchema;
            
            // Get discriminator from schema.
            if (actualType == nominalType && schema is { DiscriminatorIsRequired: false, HasRootClass: false })
                return null;
            
            if (schema is { HasRootClass: true, IsRootClass: false })
            {
                var values = new List<BsonValue>();
                do
                {
                    values.Add(schema.Discriminator);
                    schema = schema.BaseSchema;
                } while (schema is { IsRootClass: false });
                
                return new BsonArray(values.Reverse<BsonValue>()); //reverse to put leaf class last
            }

            return schema.Discriminator;
        }
    }
}
