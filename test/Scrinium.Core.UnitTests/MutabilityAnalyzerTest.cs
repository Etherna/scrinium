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

using Etherna.Scrinium.Core.Models;
using Etherna.Scrinium.Core.ProxyModels;
using System;
using System.Collections.Generic;
using Xunit;

namespace Etherna.Scrinium.Core
{
    public class MutabilityAnalyzerTest
    {
        // Types.
        private sealed class ClassWithBusinessMethod
        {
            public int Value { get; }
            public int Doubled() => Value * 2;
        }

        private sealed class ClassWithEntityReferenceMember
        {
            public FakeModel? Reference { get; init; }
        }

        private sealed class ClassWithMutableMember
        {
            public MutableClass Child { get; init; } = new();
        }

        private sealed class ImmutableClass
        {
            public ImmutableClass(int value)
            {
                Value = value;
            }

            public int Value { get; }
        }

        private sealed record ImmutableRecord(int Value, string Name);

        private sealed class InitOnlyClass
        {
            public int Value { get; init; }
        }

        private sealed class MutableClass
        {
            public int Value { get; set; }
        }

        // Data.
        public static IEnumerable<object[]> Cases =>
        [
            //immutable by getter
            [typeof(string), false],
            [typeof(int), false],
            [typeof(DateTime), false],
            [typeof(IEnumerable<string>), false],
            [typeof(IReadOnlyList<string>), false],
            [typeof(IReadOnlyDictionary<string, string>), false],
            [typeof(IEnumerable<FakeModel>), false],       //entity elements don't propagate
            [typeof(FakeModel), false],                    //entity reference
            [typeof(ImmutableClass), false],
            [typeof(InitOnlyClass), false],
            [typeof(ImmutableRecord), false],
            [typeof(ClassWithEntityReferenceMember), false],

            //exposing mutation
            [typeof(List<string>), true],
            [typeof(string[]), true],
            [typeof(Dictionary<string, string>), true],
            [typeof(IReadOnlyList<MutableClass>), true],   //read only collection of mutable elements
            [typeof(MutableClass), true],
            [typeof(ClassWithBusinessMethod), true],
            [typeof(ClassWithMutableMember), true],
        ];

        // Tests.
        [Theory]
        [MemberData(nameof(Cases))]
        public void ClassifiesTypeMutability(Type type, bool expectedExposesMutation) =>
            Assert.Equal(expectedExposesMutation, MutabilityAnalyzer.ExposesAutonomousMutation(type));
    }
}
