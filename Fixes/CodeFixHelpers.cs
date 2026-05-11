using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Derive.Fixes
{
    internal static class CodeFixHelpers
    {
        public static async Task<SyntaxNode> GetRootAsync(
            Document document,
            CancellationToken token
        ) =>
            await document.GetSyntaxRootAsync(token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Could not get root");

        // Surfaces silent-return bugs during development. Compiled out in Release so consumer
        // builds never crash the IDE if a fix encounters an unexpected state.
        [System.Diagnostics.Conditional("DEBUG")]
        public static void FailInDebug(string message) =>
            throw new InvalidOperationException(message);

        private static readonly SymbolDisplayFormat TypeFormat =
            SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
                SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
                    | SymbolDisplayMiscellaneousOptions.UseSpecialTypes
            );

        public static TypeSyntax TypeExpression(ITypeSymbol type) =>
            SyntaxFactory.ParseTypeName(type.ToDisplayString(TypeFormat));

        public static ParameterSyntax ToParameter(IParameterSymbol p)
        {
            var modifiers = new List<SyntaxToken>();
            switch (p.RefKind)
            {
                case RefKind.Ref:
                    modifiers.Add(SyntaxFactory.Token(SyntaxKind.RefKeyword));
                    break;
                case RefKind.Out:
                    modifiers.Add(SyntaxFactory.Token(SyntaxKind.OutKeyword));
                    break;
                case RefKind.In:
                    modifiers.Add(SyntaxFactory.Token(SyntaxKind.InKeyword));
                    break;
            }
            if (p.IsParams)
                modifiers.Add(SyntaxFactory.Token(SyntaxKind.ParamsKeyword));

            var paramSyntax = SyntaxFactory
                .Parameter(SyntaxFactory.Identifier(p.Name))
                .WithType(TypeExpression(p.Type));
            if (modifiers.Count > 0)
                paramSyntax = paramSyntax.WithModifiers(SyntaxFactory.TokenList(modifiers));
            return paramSyntax;
        }

        public static IEnumerable<SyntaxToken> AccessibilityModifiers(Accessibility accessibility)
        {
            switch (accessibility)
            {
                case Accessibility.Public:
                    yield return SyntaxFactory.Token(SyntaxKind.PublicKeyword);
                    break;
                case Accessibility.Protected:
                    yield return SyntaxFactory.Token(SyntaxKind.ProtectedKeyword);
                    break;
                case Accessibility.Internal:
                    yield return SyntaxFactory.Token(SyntaxKind.InternalKeyword);
                    break;
                case Accessibility.ProtectedOrInternal:
                    yield return SyntaxFactory.Token(SyntaxKind.ProtectedKeyword);
                    yield return SyntaxFactory.Token(SyntaxKind.InternalKeyword);
                    break;
            }
        }
    }
}
