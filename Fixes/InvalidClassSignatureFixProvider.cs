using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Derive.Fixes
{
    [
        ExportCodeFixProvider(
            LanguageNames.CSharp,
            Name = nameof(InvalidClassSignatureFixProvider)
        ),
        Shared
    ]
    public class InvalidClassSignatureFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds { get; } =
        [DeriveDiagnosticsConstants.InvalidClassSignatureId];

        public sealed override FixAllProvider GetFixAllProvider() =>
            WellKnownFixAllProviders.BatchFixer;

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            SyntaxNode root = await GetRoot(context.Document, context.CancellationToken)
                .ConfigureAwait(false);
            var diagnostic = context.Diagnostics.Single();
            var classDeclaration =
                root.FindNode(diagnostic.Location.SourceSpan) as ClassDeclarationSyntax;
            if (classDeclaration == null)
                return;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Make class partial",
                    createChangedDocument: token =>
                        AddPartialModifierAsync(context.Document, classDeclaration, token),
                    equivalenceKey: "MakePartial"
                ),
                diagnostic
            );
        }

        private async Task<Document> AddPartialModifierAsync(
            Document document,
            ClassDeclarationSyntax classDeclaration,
            CancellationToken token
        )
        {
            // If the class already has the modifier, just return the original document
            if (classDeclaration.Modifiers.Any(token => token.IsKind(SyntaxKind.PartialKeyword)))
                return document;

            // Insert the partial keyword after existing accessibility modifiers (public, internal, etc.)
            var modifiers = classDeclaration.Modifiers;

            // Find the position after the last accessibility modifier
            int insertPos = 0;
            for (int i = 0; i < modifiers.Count; i++)
            {
                var kind = modifiers[i].Kind();
                if (
                    kind == SyntaxKind.PublicKeyword
                    || kind == SyntaxKind.PrivateKeyword
                    || kind == SyntaxKind.InternalKeyword
                    || kind == SyntaxKind.ProtectedKeyword
                )
                {
                    insertPos = i + 1;
                }
            }

            var partialToken = SyntaxFactory
                .Token(SyntaxKind.PartialKeyword)
                .WithTrailingTrivia(SyntaxFactory.Space);
            modifiers = modifiers.Insert(insertPos, partialToken);

            var newClassDecl = classDeclaration.WithModifiers(modifiers);
            SyntaxNode root = await GetRoot(document, token).ConfigureAwait(false);
            var newRoot = root.ReplaceNode(classDeclaration, newClassDecl);
            return document.WithSyntaxRoot(newRoot);
        }

        private static async Task<SyntaxNode> GetRoot(Document document, CancellationToken token)
        {
            return await document.GetSyntaxRootAsync(token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Could not get root");
        }
    }
}
