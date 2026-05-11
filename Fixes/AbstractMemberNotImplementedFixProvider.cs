using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;

namespace Derive.Fixes
{
    [
        ExportCodeFixProvider(
            LanguageNames.CSharp,
            Name = nameof(AbstractMemberNotImplementedFixProvider)
        ),
        Shared
    ]
    public class AbstractMemberNotImplementedFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds { get; } =
        [DiagnosticIds.AbstractMemberNotImplemented];

        public sealed override FixAllProvider GetFixAllProvider() =>
            WellKnownFixAllProviders.BatchFixer;

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var diagnostic = context.Diagnostics.Single();
            var props = diagnostic.Properties;

            if (!TryGetNonEmpty(props, DiagnosticProperties.BaseTypeDocId, out var baseTypeDocId))
            {
                CodeFixHelpers.FailInDebug("DER0005 diagnostic missing BaseTypeDocId");
                return;
            }
            if (!TryGetNonEmpty(props, DiagnosticProperties.MemberDocIds, out var memberDocIds))
            {
                CodeFixHelpers.FailInDebug("DER0005 diagnostic missing MemberDocIds");
                return;
            }
            var baseTypeName = props.TryGetValue(DiagnosticProperties.BaseTypeName, out var n) ? n : null;
            var typeArgDocIds = props.TryGetValue(DiagnosticProperties.TypeArgDocIds, out var t) ? t : string.Empty;

            SyntaxNode root = await CodeFixHelpers
                .GetRootAsync(context.Document, context.CancellationToken)
                .ConfigureAwait(false);
            if (
                root.FindNode(diagnostic.Location.SourceSpan)
                is not ClassDeclarationSyntax classDeclaration
            )
            {
                CodeFixHelpers.FailInDebug("DER0005 diagnostic location did not resolve to a class declaration");
                return;
            }

            var title = string.IsNullOrEmpty(baseTypeName)
                ? "Implement derived members"
                : $"Implement '{baseTypeName}' members";

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: title,
                    createChangedDocument: token => AddStubsAsync(
                        context.Document, classDeclaration, baseTypeDocId, typeArgDocIds ?? string.Empty, memberDocIds, token),
                    equivalenceKey: "ImplementDerivedMembers"
                ),
                diagnostic
            );
        }

        private static bool TryGetNonEmpty(
            ImmutableDictionary<string, string?> props,
            string key,
            out string value
        )
        {
            if (props.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
            {
                value = v!;
                return true;
            }
            value = string.Empty;
            return false;
        }

        private static async Task<Document> AddStubsAsync(
            Document document,
            ClassDeclarationSyntax classDeclaration,
            string baseTypeDocId,
            string typeArgDocIds,
            string memberDocIds,
            CancellationToken token
        )
        {
            var model = await document.GetSemanticModelAsync(token).ConfigureAwait(false);
            if (model == null)
            {
                CodeFixHelpers.FailInDebug("No semantic model for document");
                return document;
            }
            var compilation = model.Compilation;

            if (DocumentationCommentId.GetFirstSymbolForDeclarationId(baseTypeDocId, compilation)
                is not INamedTypeSymbol openBase)
            {
                CodeFixHelpers.FailInDebug($"BaseTypeDocId '{baseTypeDocId}' did not resolve");
                return document;
            }

            var constructedBase = ConstructBase(openBase, typeArgDocIds, compilation);
            if (constructedBase == null)
                return document;

            var comparer = SymbolEqualityComparer.Default;
            var stubs = new List<MemberDeclarationSyntax>();
            foreach (var memberId in memberDocIds.Split(';'))
            {
                if (DocumentationCommentId.GetFirstSymbolForDeclarationId(memberId, compilation)
                    is not IMethodSymbol openMethod)
                {
                    CodeFixHelpers.FailInDebug($"Member docId '{memberId}' did not resolve");
                    continue;
                }
                var constructedMethod = FindMethodOn(constructedBase, openMethod, comparer);
                if (constructedMethod == null)
                {
                    CodeFixHelpers.FailInDebug($"Constructed base {constructedBase} has no member matching {openMethod}");
                    continue;
                }
                var stub = CreateImplementationStub(constructedMethod)
                    .WithAdditionalAnnotations(Formatter.Annotation, Simplifier.Annotation);
                stubs.Add(stub);
            }

            if (stubs.Count == 0)
                return document;

            var newClassDecl = classDeclaration.AddMembers([.. stubs]);
            SyntaxNode root = await CodeFixHelpers.GetRootAsync(document, token).ConfigureAwait(false);
            var newRoot = root.ReplaceNode(classDeclaration, newClassDecl);
            return document.WithSyntaxRoot(newRoot);

            // Looks up the constructed counterpart of `openMethod` on `type` by name and by
            // matching the original definition (handles overloads sharing a name).
            static IMethodSymbol? FindMethodOn(
                INamedTypeSymbol type,
                IMethodSymbol openMethod,
                SymbolEqualityComparer comparer
            )
            {
                foreach (var candidate in type.GetMembers(openMethod.Name).OfType<IMethodSymbol>())
                {
                    if (comparer.Equals(candidate.OriginalDefinition, openMethod))
                        return candidate;
                }
                return null;
            }
        }

        private static INamedTypeSymbol? ConstructBase(
            INamedTypeSymbol openBase,
            string typeArgDocIds,
            Compilation compilation
        )
        {
            if (string.IsNullOrEmpty(typeArgDocIds) || !openBase.IsGenericType)
                return openBase;

            var typeArgs = new List<ITypeSymbol>();
            foreach (var argId in typeArgDocIds.Split(';'))
            {
                if (DocumentationCommentId.GetFirstSymbolForDeclarationId(argId, compilation)
                    is not ITypeSymbol typeArg)
                {
                    CodeFixHelpers.FailInDebug($"TypeArg docId '{argId}' did not resolve");
                    return null;
                }
                typeArgs.Add(typeArg);
            }
            return openBase.Construct([.. typeArgs]);
        }

        private static MethodDeclarationSyntax CreateImplementationStub(IMethodSymbol method)
        {
            var modifiers = CodeFixHelpers.AccessibilityModifiers(method.DeclaredAccessibility);

            var throwExpr = SyntaxFactory.ThrowExpression(
                SyntaxFactory
                    .ObjectCreationExpression(
                        SyntaxFactory.ParseTypeName("global::System.NotImplementedException")
                    )
                    .WithArgumentList(SyntaxFactory.ArgumentList())
            );

            var decl = SyntaxFactory
                .MethodDeclaration(CodeFixHelpers.TypeExpression(method.ReturnType), method.Name)
                .WithModifiers(SyntaxFactory.TokenList(modifiers))
                .WithParameterList(
                    SyntaxFactory.ParameterList(
                        SyntaxFactory.SeparatedList(
                            method.Parameters.Select(CodeFixHelpers.ToParameter)
                        )
                    )
                )
                .WithExpressionBody(SyntaxFactory.ArrowExpressionClause(throwExpr))
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

            if (method.IsGenericMethod)
            {
                decl = decl.WithTypeParameterList(
                    SyntaxFactory.TypeParameterList(
                        SyntaxFactory.SeparatedList(
                            method.TypeParameters.Select(tp => SyntaxFactory.TypeParameter(tp.Name))
                        )
                    )
                );
            }

            return decl;
        }
    }
}
