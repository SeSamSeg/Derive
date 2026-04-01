using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using Derive.Generator;
using Derive.Generator.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Derive.Generator
{
    public class PartialClassExtensionBuilder
    {
        private readonly ITypeSymbol sourceType;
        private readonly List<Type> interfaces = [];
        private Action<IndentedStringBuilder> body = static _ => { };

        public PartialClassExtensionBuilder(ITypeSymbol sourceType)
        {
            this.sourceType = sourceType;
        }

        public PartialClassExtensionBuilder WithBody(Action<IndentedStringBuilder> body)
        {
            this.body = body;
            return this;
        }

        public PartialClassExtensionBuilder Implementing<T>()
            where T : class
        {
            Debug.Assert(typeof(T).IsInterface);
            interfaces.Add(typeof(T));
            return this;
        }

        public void AppendOnto(IndentedStringBuilder builder)
        {
            if (sourceType.ContainingNamespace != null)
            {
                builder.AppendLine($"namespace {sourceType.ContainingNamespace}");
                BeginBlock(builder);
            }
            if (sourceType.ContainingType != null)
            {
                StartClassDeclaration(sourceType.ContainingType, builder);
                BeginBlock(builder);
            }

            StartClassDeclaration(sourceType, builder);
            if (interfaces.Count > 0)
            {
                builder.Append(" : ");
                builder.Append(interfaces[0].Name);
                foreach (var i in interfaces.Skip(1))
                {
                    builder.Append(", ");
                    builder.Append(i.Name);
                }
            }
            BeginBlock(builder);

            body(builder);

            EndBlock(builder);
            if (sourceType.ContainingType != null)
            {
                EndBlock(builder);
            }
            if (sourceType.ContainingNamespace != null)
            {
                EndBlock(builder);
            }
        }

        private static void BeginBlock(IndentedStringBuilder builder)
        {
            builder.AppendLine("{");
            builder.IncrementIndent();
        }

        public static void EndBlock(IndentedStringBuilder builder)
        {
            builder.DecrementIndent();
            builder.AppendLine("}");
        }

        public static void StartClassDeclaration(ITypeSymbol type, IndentedStringBuilder builder)
        {
            switch (type.DeclaredAccessibility)
            {
                case Accessibility.Private:
                    builder.Append("private ");
                    break;
                case Accessibility.ProtectedAndInternal:
                    builder.Append("private protected ");
                    break;
                case Accessibility.Protected:
                    builder.Append("protected ");
                    break;
                case Accessibility.Internal:
                    builder.Append("internal ");
                    break;
                case Accessibility.ProtectedOrInternal:
                    builder.Append("protected internal ");
                    break;
                case Accessibility.Public:
                    builder.Append("public ");
                    break;
                default:
                    throw new NotImplementedException();
            }
            builder.Append("partial class ");
            builder.AppendLine(type.Name);
        }
    }
}
