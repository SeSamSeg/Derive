using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace Derive.Generator.Utils
{
    internal static class CodeAnalysisExtensions
    {
        public static IncrementalValuesProvider<TSource> WhereNotNull<TSource>(
            this IncrementalValuesProvider<TSource?> source
        ) => source.SelectMany((item, _) => item != null ? [item] : (IEnumerable<TSource>)[]);

        public static bool IsOfType<T>(this AttributeSyntax attribute)
            where T : Attribute => AttributeNameMatches(attribute, typeof(T).Name.AsSpan());

        public static IEnumerable<AttributeSyntax> OfAttributeType<T>(
            this IEnumerable<AttributeSyntax> attribute
        )
            where T : Attribute
        {
            string attributeName = typeof(T).Name;
            return attribute.Where(a => AttributeNameMatches(a, attributeName));
        }

        // Matches [Name] and [NameAttribute]; strips generic args from [Name<T>] before comparing.
        private static bool AttributeNameMatches(AttributeSyntax attribute, ReadOnlySpan<char> fullName)
        {
            const int AttributeSuffixLength = 9;
            var simpleName = (attribute.Name is GenericNameSyntax g ? g.Identifier.Text : attribute.Name.ToString()).AsSpan();
            return simpleName.Equals(fullName, StringComparison.Ordinal)
                || simpleName.Equals(fullName[..^AttributeSuffixLength], StringComparison.Ordinal);
        }

        public static bool HasCompatibleMember(this INamedTypeSymbol type, ISymbol member)
        {
            if (member is IPropertySymbol)
                return type.GetMembers(member.Name).OfType<IPropertySymbol>().Any();
            if (member is IMethodSymbol method)
                return type.GetMembers(member.Name).OfType<IMethodSymbol>()
                    .Any(m => ParametersMatch(m, method));
            return false;
        }

        public static bool ParametersMatch(IMethodSymbol a, IMethodSymbol b)
        {
            if (a.Parameters.Length != b.Parameters.Length)
                return false;
            var comparer = SymbolEqualityComparer.Default;
            for (int i = 0; i < a.Parameters.Length; i++)
                if (!comparer.Equals(a.Parameters[i].Type, b.Parameters[i].Type))
                    return false;
            return true;
        }

        public static TypeDeclarationSyntax GetTypeSyntax(
            this ITypeSymbol baseType,
            CancellationToken token
        )
        {
            if (baseType.DeclaringSyntaxReferences.Length == 0)
            {
                throw new ArgumentException("Should not call on class with no syntax references");
            }
            if (baseType.DeclaringSyntaxReferences.Length > 1)
            {
                throw new NotImplementedException();
            }
            var baseReference = baseType.DeclaringSyntaxReferences.Single();
            if (baseReference.GetSyntax(token) is not TypeDeclarationSyntax typeSyntax)
            {
                throw new InvalidOperationException($"Could not get syntax for {baseType.Name}");
            }
            return typeSyntax;
        }
    }
}
