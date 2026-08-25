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

using Etherna.Scrinium.Core.Domain.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Linq;
using Xunit;

namespace Etherna.Scrinium.Core.Generators
{
    public class ProxyModelsGeneratorTest
    {
        // Consts.
        private const string ModelSource = """
            using Etherna.Scrinium.Core.Domain.Models;
            using System.Collections.Generic;
            using System.Linq;

            namespace TestDomain
            {
                public class Cat : EntityModelBase<string>
                {
                    private List<string> _nicknames = [];

                    public virtual int Age { get; set; }
                    public virtual IEnumerable<string> Nicknames
                    {
                        get => _nicknames;
                        protected set => _nicknames = [.. value];
                    }

                    public virtual void AddNickname(string nickname) => AddNicknameHelper(nickname);
                    public virtual void Rename(string nickname) { }

                    private void AddNicknameHelper(string nickname) => _nicknames.Add(nickname);
                }

                public abstract class AnimalBase : EntityModelBase<string>
                { }

                public class NotAModel
                {
                    public virtual string? Name { get; set; }
                }
            }
            """;

        // Tests.
        [Fact]
        public void GeneratesProxiesOnlyForConcreteEntityModels()
        {
            // Action.
            var runResult = RunGenerator(ModelSource, out _);

            // Assert.
            var generatedHintNames = runResult.Results[0].GeneratedSources.Select(s => s.HintName).ToArray();
            Assert.Contains("CatProxy.g.cs", generatedHintNames);
            Assert.Contains("GeneratedProxyModelAttributes.g.cs", generatedHintNames);
            Assert.DoesNotContain("AnimalBaseProxy.g.cs", generatedHintNames);
            Assert.DoesNotContain("NotAModelProxy.g.cs", generatedHintNames);
        }

        [Fact]
        public void GeneratedProxiesCompileWithoutDiagnostics()
        {
            // Action.
            _ = RunGenerator(ModelSource, out var outputCompilation);

            // Assert.
            var errors = outputCompilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToArray();
            Assert.Empty(errors);
        }

        [Fact]
        public void ComputesAlteredMembersFromMethodBodies()
        {
            // Action.
            var runResult = RunGenerator(ModelSource, out _);

            // Assert.
            var proxySource = runResult.Results[0].GeneratedSources
                .Single(s => s.HintName == "CatProxy.g.cs").SourceText.ToString();

            //a method mutating a backing field through a private helper alters its property
            Assert.Contains("""
                public override void AddNickname(string nickname)
                        {
                            OnProxyMethodInvoke(new[] { "Nicknames" });
                """.ReplaceLineEndings(), proxySource.ReplaceLineEndings(), StringComparison.Ordinal);

            //a method touching no backing field alters nothing
            Assert.Contains("""
                public override void Rename(string nickname)
                        {
                            OnProxyMethodInvoke(global::System.Array.Empty<string>());
                """.ReplaceLineEndings(), proxySource.ReplaceLineEndings(), StringComparison.Ordinal);
        }

        [Fact]
        public void DoesntProxyTheIdMember()
        {
            // Action.
            var runResult = RunGenerator(ModelSource, out _);

            // Assert.
            var proxySource = runResult.Results[0].GeneratedSources
                .Single(s => s.HintName == "CatProxy.g.cs").SourceText.ToString();

            //identity is definitionally present and immutable: no interception, no merges
            Assert.DoesNotContain("override string Id", proxySource, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Id\"", proxySource, StringComparison.Ordinal);

            //the id keeps driving the lazy load, and other members stay proxied
            Assert.Contains("base.Id", proxySource, StringComparison.Ordinal);
            Assert.Contains("public override int Age", proxySource, StringComparison.Ordinal);
        }

        [Fact]
        public void RegistersProxiesAtAssemblyLevel()
        {
            // Action.
            var runResult = RunGenerator(ModelSource, out _);

            // Assert.
            var registrationsSource = runResult.Results[0].GeneratedSources
                .Single(s => s.HintName == "GeneratedProxyModelAttributes.g.cs").SourceText.ToString();
            Assert.Contains(
                "[assembly: global::Etherna.Scrinium.Core.ProxyModels.GeneratedProxyModel(typeof(global::TestDomain.Cat), typeof(global::TestDomain.CatProxy))]",
                registrationsSource,
                StringComparison.Ordinal);
        }

        // Helpers.
        private static GeneratorDriverRunResult RunGenerator(string modelSource, out Compilation outputCompilation)
        {
            /* Compile the model source against the Scrinium core and the runtime assemblies,
             * run the generator on it, and return both the run result and the augmented
             * compilation, for asserts on the emitted sources and on their compilation. */
            var references = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                .Select(assembly => (MetadataReference)MetadataReference.CreateFromFile(assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(IEntityModel).Assembly.Location));

            var compilation = CSharpCompilation.Create(
                "TestDomain",
                [CSharpSyntaxTree.ParseText(modelSource)],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

            var driver = CSharpGeneratorDriver.Create(new ProxyModelsGenerator());
            return driver.RunGeneratorsAndUpdateCompilation(compilation, out outputCompilation, out _)
                .GetRunResult();
        }
    }
}
