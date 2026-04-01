using System.Collections.Immutable;
using Derive.Generator.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Derive.Generator
{
    internal static class DeclarationHelpers
    {
        internal static IncrementalValueProvider<ImmutableArray<string>> ProvideUsings(
            IncrementalGeneratorInitializationContext context
        )
        {
            return context
                .SyntaxProvider.CreateSyntaxProvider(
                    predicate: static (node, _) =>
                        node
                            is UsingDirectiveSyntax
                        {
                            GlobalKeyword.RawKind: not 0,
                            Name: not null
                        },
                    transform: static (ctx, _) =>
                    {
                        var usingDirective = (UsingDirectiveSyntax)ctx.Node;
                        return usingDirective.Name!.ToString();
                    }
                )
                .Collect();
        }
    }
}
