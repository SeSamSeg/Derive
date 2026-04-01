using System.Text;
using Microsoft.CodeAnalysis;

namespace Derive.Generator
{
    internal static class Namings
    {
        public const string GeneratedNamespace = "Derive.Generated";

        /// <summary>
        /// Get a name for the type that includes:
        /// <list type="bullet">
        /// <item>The defining assembly</item>
        /// <item>The full name including namespace</item>
        /// <item>The amount of generic parameters</item>
        /// </list>
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public static string GetFullMetadataName(INamedTypeSymbol type)
        {
            var assemblyName =
                type.ContainingAssembly?.Name
                ?? throw new InvalidOperationException(
                    $"Cannot generate full type info for {type.Name} as it is missing "
                );
            var sb = new StringBuilder();
            sb.Append('[').Append(assemblyName).Append(']');
            var name = type.ToDisplayString(
                new SymbolDisplayFormat(
                    typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                    genericsOptions: SymbolDisplayGenericsOptions.None
                )
            );
            sb.Append(name);
            if (type.TypeArguments.Any())
            {
                sb.Append('`');
                sb.Append(type.TypeArguments.Count());
            }
            return sb.ToString();
        }

        public static string GetDeterministicHashIdentifier(string text) =>
            $"H{GetDeterministicHashCode(text):X8}"; // Prefixed with H for use as identifier

        private static int GetDeterministicHashCode(string str)
        {
            unchecked
            {
                int hash1 = (5381 << 16) + 5381;
                int hash2 = hash1;

                for (int i = 0; i < str.Length; i += 2)
                {
                    hash1 = ((hash1 << 5) + hash1) ^ str[i];
                    if (i == str.Length - 1)
                        break;
                    hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
                }

                return hash1 + (hash2 * 1566083941);
            }
        }
    }
}
