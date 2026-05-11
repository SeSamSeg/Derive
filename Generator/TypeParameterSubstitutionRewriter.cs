using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Derive.Generator
{
    /// <summary>
    /// Rewrites a syntax tree by replacing references to generic type parameters with the
    /// concrete type-argument syntax supplied at construction time. Used to specialize a
    /// base type's syntax (e.g. <c>Foo&lt;T&gt;</c>) to a constructed form (<c>Foo&lt;int&gt;</c>).
    /// </summary>
    /// <remarks>
    /// Matching is name-based: any <see cref="IdentifierNameSyntax"/> whose identifier text
    /// equals a known type-parameter name is replaced. This means a member-access or
    /// variable whose name happens to collide with a type parameter (for example a static
    /// method named <c>T</c>) will also be rewritten. Switch to a semantic-model based
    /// approach if that becomes an issue.
    /// </remarks>
    internal sealed class TypeParameterSubstitutionRewriter : CSharpSyntaxRewriter
    {
        private readonly Dictionary<string, TypeSyntax> map;

        public TypeParameterSubstitutionRewriter(Dictionary<string, TypeSyntax> map)
        {
            this.map = map;
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (map.TryGetValue(node.Identifier.Text, out var replacement) && IsTypeReference(node))
            {
                return replacement.WithTriviaFrom(node);
            }
            return base.VisitIdentifierName(node);
        }

        private static bool IsTypeReference(IdentifierNameSyntax node)
        {
            switch (node.Parent)
            {
                case MemberAccessExpressionSyntax mae when mae.Name == node: // RHS of '.'
                case MemberBindingExpressionSyntax mbe when mbe.Name == node: // RHS of '?.'
                case NameColonSyntax: // named argument label
                case NameEqualsSyntax: // attribute / initializer name
                case InvocationExpressionSyntax inv when inv.Expression == node: // bare call T(...)
                case NameMemberCrefSyntax nmc when nmc.Name == node: // XML doc member ref
                    return false;
                default:
                    return true;
            }
        }
    }
}
