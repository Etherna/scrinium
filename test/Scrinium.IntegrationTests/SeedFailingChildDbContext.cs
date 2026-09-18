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

using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.Serialization;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Etherna.Scrinium.IntegrationTests
{
    public interface ISeedFailingChildDbContext : IDbContext
    { }

    /// <summary>
    /// Writable child of <see cref="ISeedFailingParentDbContext"/>, never seeded: its seed
    /// counts its attempts and fails.
    /// </summary>
    internal sealed class SeedFailingChildDbContext(
        ConcurrentDictionary<string, object?> seedingObservations)
        : DbContext, ISeedFailingChildDbContext
    {
        // Consts.
        public const string SeedAttemptsKey = "failingChildSeedAttempts";

        // Protected properties.
        protected override IEnumerable<IModelMapsCollector> ModelMapsCollectors => [];

        // Protected methods.
        protected override Task SeedAsync()
        {
            seedingObservations.AddOrUpdate(SeedAttemptsKey, 1, (_, attempts) => (int)attempts! + 1);
            throw new InvalidOperationException("The seed of the child fails");
        }
    }
}
