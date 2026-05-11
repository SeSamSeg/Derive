using System.Collections.Immutable;
using System.Text;
using Derive.Generator.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Derive.Generator
{
    [Generator]
    public class Deriver : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Step 1: Find classes with our marker attribute
            var deriverInfo = context
                .SyntaxProvider.CreateSyntaxProvider(
                    predicate: static (node, _) =>
                        node is ClassDeclarationSyntax cds && cds.AttributeLists.Count > 0,
                    transform: static (ctx, _) => GetClassInfo(ctx)
                )
                .WhereNotNull();

            IncrementalValueProvider<ImmutableArray<string>> globalUsings =
                DeclarationHelpers.ProvideUsings(context);

            context.RegisterSourceOutput(deriverInfo.Combine(globalUsings), Generate);
        }

        private static ClassDeriveInfo? GetClassInfo(GeneratorSyntaxContext context)
        {
            var classDecl = (ClassDeclarationSyntax)context.Node;

            // Look for an attribute named "Derive"
            var attributes = classDecl
                .AttributeLists.SelectMany(al => al.Attributes)
                .OfAttributeType<DeriveAttribute>()
                .ToArray();
            AttributeSyntax attribute;
            switch (attributes.Length)
            {
                case 0:
                    return null;
                case 1:
                    attribute = attributes[0];
                    break;
                default:
                    // TODO: Diagnostics?
                    throw new NotImplementedException("More than one Derive attribute detected");
            }

            if (attribute.ArgumentList == null)
            {
                // Nothing to derive from
                return null;
            }

            var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

            // Extract the type argument of Derive(typeof(SomeBase))
            int derivedCount = attribute.ArgumentList.Arguments.Count;
            if (derivedCount == 0)
                return null;

            var baseTypes = new ITypeSymbol[derivedCount];
            for (int i = 0; i < attribute.ArgumentList.Arguments.Count; i++)
            {
                AttributeArgumentSyntax arg = attribute.ArgumentList.Arguments[i];
                if (arg.Expression is not TypeOfExpressionSyntax tos)
                {
                    diagnostics.Add(
                        Diagnostic.Create(
                            Descriptors.InvalidDeriveArguments,
                            classDecl.GetLocation(),
                            "use TypeOf expressions as arguments"
                        )
                    );
                    continue;
                }
                if (context.SemanticModel.GetTypeInfo(tos.Type).Type is not ITypeSymbol type)
                {
                    throw new InvalidOperationException(
                        $"Could not process typeof expression {tos}"
                    );
                }
                baseTypes[i] = type;
            }

            if (context.SemanticModel.GetDeclaredSymbol(classDecl) is not ITypeSymbol classType)
            {
                return null;
            }

            if (!classDecl.Modifiers.Any(t => t.Text.Equals("partial", StringComparison.Ordinal)))
            {
                diagnostics.Add(
                    Diagnostic.Create(
                        Descriptors.InvalidClassSignature,
                        classDecl.GetLocation(),
                        "partial"
                    )
                );
            }

            var classNamespace = GetNamespace(classDecl);

            return new(classType, baseTypes, diagnostics.ToImmutable());
        }

        private static string? GetNamespace(ClassDeclarationSyntax classDecl)
        {
            // Walk up the syntax tree until we hit a NamespaceDeclarationSyntax
            var parent = classDecl.Parent;
            while (parent != null)
            {
                // Handle regular (block‑scoped) namespaces
                if (parent is NamespaceDeclarationSyntax ns)
                {
                    return ns.Name.ToString();
                }

                // Handle file‑scoped namespaces (C# 10+)
                if (parent is FileScopedNamespaceDeclarationSyntax fns)
                {
                    return fns.Name.ToString();
                }

                parent = parent.Parent;
            }

            // No explicit namespace – the class is in the global namespace
            return null;
        }

        private static void Generate(
            SourceProductionContext spc,
            (ClassDeriveInfo, ImmutableArray<string>) tuple
        )
        {
            var (classDeriveInfo, globalUsings) = tuple;
            foreach (var diagnostic in classDeriveInfo.Diagnostics)
            {
                spc.ReportDiagnostic(diagnostic);
            }
            foreach (var baseType in classDeriveInfo.BaseTypes)
            {
                GenerateForBase(spc, classDeriveInfo.Type, baseType);
            }
        }

        private static void GenerateForBase(
            SourceProductionContext spc,
            ITypeSymbol type,
            ITypeSymbol baseType
        )
        {
            SyntaxNode root;
            TypeDeclarationSyntax baseTypeSyntax;

            if (baseType.DeclaringSyntaxReferences.Length == 0)
            {
                if (!TryGetBaseSyntaxFromMetadata(spc, type, baseType, out root, out baseTypeSyntax))
                    return;
            }
            else if (baseType.DeclaringSyntaxReferences.Length > 1)
            {
                spc.ReportDiagnostic(
                    Diagnostic.Create(Descriptors.PartialBaseType, type.Locations[0], type.Name)
                );
                return;
            }
            else
            {
                baseTypeSyntax = baseType.GetTypeSyntax(spc.CancellationToken);
                root = baseTypeSyntax.SyntaxTree.GetRoot(spc.CancellationToken);
            }

            var usingsToCopy = root.ChildNodes().OfType<UsingDirectiveSyntax>().ToArray();
            var membersToCopy = baseTypeSyntax
                .Members.OfType<MethodDeclarationSyntax>()
                .ToArray();
            var baseList = baseTypeSyntax
                .BaseList;

            if (membersToCopy.Length == 0 && baseList == null)
                return;

            var typeArgRewriter = CreateTypeParameterSubstitution(baseType);

            var builder = new IndentedStringBuilder();
            if (usingsToCopy.Length > 0)
            {
                foreach (var @using in usingsToCopy)
                {
                    builder.AppendLine(@using.ToString());
                }
                builder.AppendLine();
            }

            new PartialClassExtensionBuilder(type)
                .WithBody(builder =>
                {
                    foreach (var m in membersToCopy)
                    {
                        var node = typeArgRewriter?.Visit(m) ?? m;
                        builder.AppendLine(node.ToString());
                    }
                }).WithTypeBase(builder =>
                {
                    if (baseList != null)
                    {
                        var node = typeArgRewriter?.Visit(baseList) ?? baseList;
                        builder.Append(node.ToString());
                    }
                }).AppendOnto(builder);

            spc.AddSource(
                $"{type.Name}_{baseType.Name}_Derived.g.cs",
                SourceText.From(builder.ToString(), Encoding.UTF8)
            );
        }

        /// <summary>
        /// Pulls the serialized base-class source emitted by <see cref="BaseSerializer"/>
        /// from <c>Derive.Generated.BaseSources</c> in the base type's assembly.
        /// Returns null on success, or a user-facing reason string on user-fixable failures.
        /// Throws when the metadata is in an unexpected internal state.
        /// </summary>
        private static string? LoadSerializedBaseSyntax(
            INamedTypeSymbol baseType,
            CancellationToken cancellationToken,
            out SyntaxNode root,
            out TypeDeclarationSyntax typeSyntax
        )
        {
            root = null!;
            typeSyntax = null!;

            string assemblyName = baseType.ContainingAssembly?.Name ?? "<unknown>";
            string sourcesTypeName = $"{Namings.GeneratedNamespace}.BaseSources";
            var sourcesType = baseType.ContainingAssembly?.GetTypeByMetadataName(sourcesTypeName);
            if (sourcesType is null)
            {
                return $"is in assembly '{assemblyName}' which does not run the BaseSerializer source generator (no '{sourcesTypeName}' found)";
            }

            string identifier = Namings.GetDeterministicHashIdentifier(
                Namings.GetFullMetadataName(baseType)
            );
            var field = sourcesType.GetMembers(identifier).OfType<IFieldSymbol>().SingleOrDefault();
            if (field is null)
            {
                return $"is not attributed as Base in assembly '{assemblyName}'";
            }
            if (!field.HasConstantValue || field.ConstantValue is not string source)
            {
                throw new InvalidOperationException(
                    $"'{sourcesTypeName}.{identifier}' in assembly '{assemblyName}' is not a string constant."
                );
            }

            root = CSharpSyntaxTree
                .ParseText(source, cancellationToken: cancellationToken)
                .GetRoot(cancellationToken);
            var typeDecl = root.DescendantNodes().OfType<TypeDeclarationSyntax>().SingleOrDefault();
            if (typeDecl is null)
            {
                throw new InvalidOperationException(
                    $"No type declaration in serialized source for '{baseType.Name}' in assembly '{assemblyName}'."
                );
            }
            typeSyntax = typeDecl;
            return null;
        }

        private static bool TryGetBaseSyntaxFromMetadata(
            SourceProductionContext spc,
            ITypeSymbol type,
            ITypeSymbol baseType,
            out SyntaxNode root,
            out TypeDeclarationSyntax baseTypeSyntax
        )
        {
            root = null!;
            baseTypeSyntax = null!;

            if (baseType is not INamedTypeSymbol namedBaseType)
            {
                ReportError(
                    $"({baseType.Kind}) is not a named type and cannot be loaded from another assembly"
                );
                return false;
            }

            var baseTypeError = LoadSerializedBaseSyntax(
                namedBaseType,
                spc.CancellationToken,
                out root,
                out baseTypeSyntax
            );
            if (baseTypeError is not null)
            {
                ReportError(baseTypeError);
                return false;
            }

            return true;

            void ReportError(string reason) =>
                spc.ReportDiagnostic(
                    Diagnostic.Create(
                        Descriptors.PublicBaseTypeNotAttributed,
                        type.Locations[0],
                        type.Name,
                        baseType.Name,
                        reason
                    )
                );
        }

        private sealed record ClassDeriveInfo(
            ITypeSymbol Type,
            ITypeSymbol[] BaseTypes,
            ImmutableArray<Diagnostic> Diagnostics
        );

        private static TypeParameterSubstitutionRewriter? CreateTypeParameterSubstitution(
            ITypeSymbol baseType
        )
        {
            if (baseType is not INamedTypeSymbol { IsGenericType: true } named)
                return null;

            var parameters = named.OriginalDefinition.TypeParameters;
            var arguments = named.TypeArguments;
            if (parameters.Length != arguments.Length)
            {
                throw new InvalidOperationException(
                    $"Type parameter/argument arity mismatch on '{named}': "
                        + $"{parameters.Length} parameters vs {arguments.Length} arguments."
                );
            }
            if (parameters.Length == 0)
                return null;

            var symbolComparer = SymbolEqualityComparer.Default;
            var map = new Dictionary<string, TypeSyntax>(parameters.Length);
            for (int i = 0; i < parameters.Length; i++)
            {
                if (symbolComparer.Equals(parameters[i], arguments[i]))
                    continue;
                map[parameters[i].Name] = SyntaxFactory.ParseTypeName(
                    arguments[i].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                );
            }
            return map.Count == 0 ? null : new TypeParameterSubstitutionRewriter(map);
        }
    }
}
