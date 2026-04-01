using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Derive.Generator.Utils
{
    internal static class CodeAnalysisExtensions
    {
        public static IncrementalValuesProvider<TSource> WhereNotNull<TSource>(
            this IncrementalValuesProvider<TSource?> source
        ) => source.SelectMany((item, _) => item != null ? [item] : (IEnumerable<TSource>)[]);

        public static bool IsOfType<T>(this AttributeSyntax attribute)
            where T : Attribute
        {
            const int AttributeNameLength = 9; // Length of the Attribute type name
            var name = attribute.Name.ToString();
            var match = typeof(T).Name;
            return name.Equals(match, StringComparison.Ordinal)
                || name.Equals(match[..^AttributeNameLength], StringComparison.Ordinal);
        }

        public static IEnumerable<AttributeSyntax> OfAttributeType<T>(
            this IEnumerable<AttributeSyntax> attribute
        )
            where T : Attribute
        {
            const int AttributeNameLength = 9; // Length of the Attribute type name
            string attributeName = typeof(T).Name;
            return attribute.Where(a =>
            {
                var aName = a.Name.ToString().AsSpan();
                return aName.Equals(typeof(T).Name, StringComparison.Ordinal)
                    || aName.Equals(
                        typeof(T).Name.AsSpan()[..^AttributeNameLength],
                        StringComparison.Ordinal
                    );
            });
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
