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

using Etherna.MongoDB.Bson;
using Etherna.MongoDB.Bson.IO;
using Etherna.MongoDB.Bson.Serialization;
using Etherna.MongoDB.Bson.Serialization.Conventions;
using Etherna.MongoDB.Bson.Serialization.Serializers;
using Etherna.Scrinium.Core.ExecContext;
using Etherna.Scrinium.Core.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Etherna.Scrinium.Core.Conventions
{
    public class HierarchicalProxyTolerantDiscriminatorConvention : IHierarchicalDiscriminatorConvention
    {
        // Fields.
        private readonly IDbContextEngine? _dbContextEngine; //remove nullability with constructors that don't ask it, when will be possible
        private readonly IExecutionContext? executionContext;
        private readonly IDiscriminatorConvention hierarchicalDriverConvention;
        private readonly IDiscriminatorConvention objectDriverConvention;

        // Constructors.
        public HierarchicalProxyTolerantDiscriminatorConvention(
            IDbContextEngine dbContextEngine,
            string elementName)
            : this(elementName)
        {
            _dbContextEngine = dbContextEngine;
        }

        /// <summary>
        /// Only needed for static registration on <see cref="object"/>, used when dbcontext is not available.
        /// Remove when <see cref="BsonSerializer.LookupDiscriminatorConvention(Type)"/> static call will be removed.
        /// </summary>
        /// <param name="elementName">Discriminator element name</param>
        /// <param name="executionContext">Execution context</param>
        public HierarchicalProxyTolerantDiscriminatorConvention(
            string elementName,
            IExecutionContext executionContext)
            : this(elementName)
        {
            this.executionContext = executionContext;
        }

        [SuppressMessage("Usage", "CA2249:Consider using \'string.Contains\' instead of \'string.IndexOf\'")]
        [SuppressMessage("Globalization", "CA1307:Specify StringComparison for clarity")]
        private HierarchicalProxyTolerantDiscriminatorConvention(
            string elementName)
        {
            ElementName = elementName ?? throw new ArgumentNullException(nameof(elementName));
            if (elementName.IndexOf('\0') != -1)
                throw new ArgumentException("Element names cannot contain nulls.", nameof(elementName));

            hierarchicalDriverConvention = new HierarchicalDiscriminatorConvention(elementName);
            objectDriverConvention = new ObjectDiscriminatorConvention(elementName);
        }

        // Properties.
        /// <summary>
        /// The db context engine resolving discriminators: the injected one, or the engine of the
        /// db operation running on the current flow, when the convention is invoked through a
        /// static driver lookup. Null when no db operation is running on the current flow.
        /// </summary>
        public IDbContextEngine? DbContextEngine =>
            _dbContextEngine ??
            (executionContext is null ?
                null :
                DbExecutionContextHandler.TryGetCurrentDbContextEngine(executionContext));
        public string ElementName { get; }

        // Methods.
        public Type GetActualType(IBsonReader bsonReader, Type nominalType)
        {
            ArgumentNullException.ThrowIfNull(bsonReader);
            ArgumentNullException.ThrowIfNull(nominalType);

            var dbContextEngine = DbContextEngine;
            if (dbContextEngine is null)
                return GetDriverConvention(nominalType).GetActualType(bsonReader, nominalType);

            //the BsonReader is sitting at the value whose actual type needs to be found
            var bsonType = bsonReader.GetCurrentBsonType();
            if (bsonType == BsonType.Document)
            {
                //we can skip looking for a discriminator if nominalType has no discriminated sub types
                if (dbContextEngine.DiscriminatorRegistry.IsTypeDiscriminated(nominalType))
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
                        actualType = dbContextEngine.DiscriminatorRegistry.LookupActualType(nominalType, discriminator);
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
            ArgumentNullException.ThrowIfNull(nominalType);

            var dbContextEngine = DbContextEngine;
            if (dbContextEngine is null)
                return GetDriverConvention(nominalType).GetDiscriminator(nominalType, actualType);

            // Remove proxy type.
            actualType = dbContextEngine.ProxyGenerator.PurgeProxyType(actualType);

            // Find active schema for model type.
            if (!dbContextEngine.MapRegistry.TryGetModelMap(actualType, out var modelMap))
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

        // Helpers.
        /* The static registration on typeof(object) is inherited by every type of the process,
         * MongODM models and foreign ones alike, and the driver caches the resolved convention
         * for each type forever. Without a db context engine on the current flow there is no
         * MongODM discriminator registry to resolve, and the type is served with the same
         * driver convention it would get with no registration on typeof(object).
         *
         * This mirrors the outcome of the driver's own `BsonSerializer
         * .LookupDiscriminatorConvention`, verified against it: `typeof(object)` and the
         * interfaces resolve the object convention, while a class resolves the hierarchical
         * one, registered for it when its class map serializer is built.
         *
         * The selection can't delegate to that lookup: a type without a convention of its own
         * inherits the one of `typeof(object)`, which in a MongODM process is this convention,
         * so asking the driver would resolve back here. */
        private IDiscriminatorConvention GetDriverConvention(Type nominalType) =>
            nominalType == typeof(object) || nominalType.IsInterface ?
                objectDriverConvention :
                hierarchicalDriverConvention;
    }
}
