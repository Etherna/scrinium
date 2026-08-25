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

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Etherna.Scrinium.Core.Generators
{
    /// <summary>
    /// Computes, for each model method, the properties it alters: the members whose backing
    /// fields the method body touches, directly or through non virtual helpers of the model.
    /// The proxy overrides use the
    /// computed list as the lazy load trigger. Property accesses don't need analysis, since
    /// they dispatch through the proxy overrides at runtime. A method without analyzable
    /// source returns null: the proxy treats it conservatively, loading the full document
    /// when invoked on a summary model.
    /// </summary>
    internal sealed class AlteredMembersAnalyzer
    {
        // Consts.
        private const string FrameworkModelsNamespace = "Etherna.Scrinium.Core.Domain.Models";

        // Fields.
        private readonly Dictionary<IMethodSymbol, List<string>?> analyzedMethods = new(SymbolEqualityComparer.Default);
        private readonly Compilation compilation;
        private readonly Dictionary<IFieldSymbol, string> fieldToPropertyMap;
        private readonly HashSet<INamedTypeSymbol> hierarchyTypes = new(SymbolEqualityComparer.Default);
        private readonly HashSet<IMethodSymbol> visitingMethods = new(SymbolEqualityComparer.Default);

        // Constructor.
        public AlteredMembersAnalyzer(INamedTypeSymbol modelSymbol, Compilation compilation)
        {
            this.compilation = compilation;
            for (var type = modelSymbol; type is not null && type.SpecialType != SpecialType.System_Object; type = type.BaseType)
                hierarchyTypes.Add(type);
            fieldToPropertyMap = BuildFieldToPropertyMap(modelSymbol);
        }

        // Methods.
        /// <summary>
        /// The properties altered by the method: an empty list for a method touching no
        /// backing field, null for a method with no analyzable source.
        /// </summary>
        public List<string>? ComputeAlteredMemberNames(IMethodSymbol method)
        {
            if (analyzedMethods.TryGetValue(method, out var cachedNames))
                return cachedNames;

            //a cycle contributes nothing beyond the members already collected by the callers
            if (!visitingMethods.Add(method))
                return [];

            List<string>? alteredNames;
            try
            {
                alteredNames = AnalyzeMethod(method);
            }
            finally
            {
                visitingMethods.Remove(method);
            }

            analyzedMethods[method] = alteredNames;
            return alteredNames;
        }

        // Helpers.
        private List<string>? AnalyzeMethod(IMethodSymbol method)
        {
            /* Methods without source can't be analyzed: the framework model bases are known
             * to alter nothing, anything else stays unknown. */
            if (method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not MethodDeclarationSyntax methodSyntax)
                return method.ContainingNamespace.ToDisplayString() == FrameworkModelsNamespace ?
                    [] :
                    null;

            var bodyNode = (SyntaxNode?)methodSyntax.Body ?? methodSyntax.ExpressionBody?.Expression;
            if (bodyNode is null)
                return null;

            var semanticModel = compilation.GetSemanticModel(methodSyntax.SyntaxTree);
            var alteredNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in bodyNode.DescendantNodesAndSelf())
            {
                switch (node)
                {
                    //any reference to a mapped backing field alters its property
                    case IdentifierNameSyntax identifier:
                        if (semanticModel.GetSymbolInfo(identifier).Symbol is IFieldSymbol field &&
                            fieldToPropertyMap.TryGetValue(field, out var propertyName))
                            alteredNames.Add(propertyName);
                        break;

                    //non virtual helpers analyze recursively; virtual methods dispatch
                    //through the proxy overrides at runtime
                    case InvocationExpressionSyntax invocation:
                        if (semanticModel.GetSymbolInfo(invocation).Symbol is
                                IMethodSymbol { IsStatic: false, IsVirtual: false, IsOverride: false } invokedMethod &&
                            invokedMethod.ContainingType is { } containingType &&
                            hierarchyTypes.Contains(containingType))
                        {
                            var invokedAlteredNames = ComputeAlteredMemberNames(invokedMethod);
                            if (invokedAlteredNames is null)
                                return null;
                            foreach (var name in invokedAlteredNames)
                                alteredNames.Add(name);
                        }
                        break;

                    default: break;
                }
            }

            return [.. alteredNames];
        }

        private Dictionary<IFieldSymbol, string> BuildFieldToPropertyMap(INamedTypeSymbol modelSymbol)
        {
            /* Learn which backing field serves which property from the getter bodies:
             * "=> field", "get => field;" and "get { return field; }" forms. */
            var map = new Dictionary<IFieldSymbol, string>(SymbolEqualityComparer.Default);
            for (var type = modelSymbol; type is not null && type.SpecialType != SpecialType.System_Object; type = type.BaseType)
            {
                foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
                {
                    if (property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not PropertyDeclarationSyntax propertySyntax)
                        continue;

                    var getterExpression =
                        propertySyntax.ExpressionBody?.Expression ??
                        (propertySyntax.AccessorList?.Accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration)) is { } getter ?
                            getter.ExpressionBody?.Expression ??
                            (getter.Body?.Statements.FirstOrDefault() as ReturnStatementSyntax)?.Expression :
                            null);
                    if (getterExpression is null)
                        continue;

                    if (compilation.GetSemanticModel(getterExpression.SyntaxTree).GetSymbolInfo(getterExpression).Symbol
                            is IFieldSymbol backingField &&
                        !map.ContainsKey(backingField))
                        map[backingField] = property.Name;
                }
            }
            return map;
        }
    }
}
