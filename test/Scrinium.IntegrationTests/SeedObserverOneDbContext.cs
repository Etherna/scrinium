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

using Etherna.MongODM.Core;
using Etherna.MongODM.Core.Serialization;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Etherna.MongODM.IntegrationTests
{
    public interface ISeedObserverOneDbContext : IDbContext
    { }

    /// <summary>
    /// Db context left unseeded by the fixture, seeded by the startup seeding tests.
    /// Its seed records the ambient execution context items observed while seeding.
    /// </summary>
    internal sealed class SeedObserverOneDbContext(
        ConcurrentDictionary<string, IDictionary<object, object?>?> seedingObservations)
        : DbContext, ISeedObserverOneDbContext
    {
        // Protected properties.
        protected override IEnumerable<IModelMapsCollector> ModelMapsCollectors => [];

        // Protected methods.
        protected override Task SeedAsync()
        {
            seedingObservations[nameof(SeedObserverOneDbContext)] = Engine.ExecutionContext.Items;
            return Task.CompletedTask;
        }
    }
}
