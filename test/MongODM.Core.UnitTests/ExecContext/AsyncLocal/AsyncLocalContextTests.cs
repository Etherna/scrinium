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

using System.Threading.Tasks;
using Xunit;

namespace Etherna.MongODM.Core.ExecContext.AsyncLocal
{
    public class AsyncLocalContextTests
    {
        private readonly AsyncLocalContext asyncLocalContext = new();

        [Fact]
        public void ItemsNullAtCreation()
        {
            // Assert.
            Assert.Null(asyncLocalContext.Items);
        }

        [Fact]
        public async Task AsyncLocalLifeCycle()
        {
            await Task.Run(async () =>
            {
                // Action.
                asyncLocalContext.InitAsyncLocalContext();

                // Assert.
                Assert.NotNull(asyncLocalContext.Items);
                await Task.Run(() =>
                {
                    Assert.NotNull(asyncLocalContext.Items);
                });
            });

            // Assert.
            Assert.Null(asyncLocalContext.Items);
        }

        [Fact]
        public void SyncLocalLifeCycle()
        {
            void localMethod()
            {
                // Action.
                asyncLocalContext.InitAsyncLocalContext();

                // Assert.
                Assert.NotNull(asyncLocalContext.Items);
                void subLocalMethod()
                {
                    Assert.NotNull(asyncLocalContext.Items);
                }
                subLocalMethod();
            }
            localMethod();

            // Assert.
            /* Outside of an async invoker, the container is not automatically disposed. */
            Assert.NotNull(asyncLocalContext.Items);
        }

        [Fact]
        public void ContextDispose()
        {
            // Action.
            using (var handler = asyncLocalContext.InitAsyncLocalContext())
            {
                // Assert.
                Assert.NotNull(asyncLocalContext.Items);
            }

            // Assert.
            Assert.Null(asyncLocalContext.Items);
        }

        [Fact]
        public void NestedInitializationCreatesIsolatedContext()
        {
            // Action.
            using (var handler0 = AsyncLocalContext.Instance.InitAsyncLocalContext())
            {
                var outerItems = asyncLocalContext.Items!;
                outerItems.Add("outerKey", "outerValue");

                using (var handler1 = AsyncLocalContext.Instance.InitAsyncLocalContext())
                {
                    // Assert.
                    //inner context is new and isolated
                    Assert.NotNull(asyncLocalContext.Items);
                    Assert.NotSame(outerItems, asyncLocalContext.Items);
                    Assert.False(asyncLocalContext.Items!.ContainsKey("outerKey"));

                    asyncLocalContext.Items.Add("innerKey", "innerValue");
                }

                // Assert.
                //outer context is restored, untouched by inner one
                Assert.Same(outerItems, asyncLocalContext.Items);
                Assert.True(asyncLocalContext.Items!.ContainsKey("outerKey"));
                Assert.False(asyncLocalContext.Items.ContainsKey("innerKey"));
            }

            Assert.Null(asyncLocalContext.Items);
        }

        [Fact]
        public void SequentialInitializationsOverInheritedContextAreIsolated()
        {
            /* Simulate a background job runner: the worker flow inherits an initialized
             * context from an ancestor flow, and each job opens and disposes its own. */
            using var inheritedHandler = asyncLocalContext.InitAsyncLocalContext();
            var inheritedItems = asyncLocalContext.Items!;

            // Action.
            //first job
            using (var job0Handler = asyncLocalContext.InitAsyncLocalContext())
            {
                asyncLocalContext.Items!.Add("job0Key", "job0Value");
            }

            //second job
            using (var job1Handler = asyncLocalContext.InitAsyncLocalContext())
            {
                // Assert.
                //second job doesn't see first job's items
                Assert.NotSame(inheritedItems, asyncLocalContext.Items);
                Assert.False(asyncLocalContext.Items!.ContainsKey("job0Key"));
            }

            // Assert.
            //inherited context is restored
            Assert.Same(inheritedItems, asyncLocalContext.Items);
        }

        [Fact]
        public void DoubleDisposeDoesntRestoreTwice()
        {
            // Setup.
            using var rootHandler = asyncLocalContext.InitAsyncLocalContext();
            var rootItems = asyncLocalContext.Items!;

            var innerHandler = asyncLocalContext.InitAsyncLocalContext();
            innerHandler.Dispose();

            using var currentHandler = asyncLocalContext.InitAsyncLocalContext();
            var currentItems = asyncLocalContext.Items!;

            // Action.
            innerHandler.Dispose();

            // Assert.
            //second dispose of an already disposed handler doesn't replace the current context
            Assert.Same(currentItems, asyncLocalContext.Items);
            Assert.NotSame(rootItems, asyncLocalContext.Items);
        }
    }
}
