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

            var baseTypes = ImmutableArray.CreateBuilder<INamedTypeSymbol>(derivedCount);
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
                var typeInfo = context.SemanticModel.GetTypeInfo(tos.Type).Type;
                if (typeInfo is null)
                {
                    throw new InvalidOperationException(
                        $"Could not process typeof expression {tos}"
                    );
                }
                if (typeInfo is not INamedTypeSymbol named)
                {
                    diagnostics.Add(
                        Diagnostic.Create(
                            Descriptors.InvalidDeriveArguments,
                            classDecl.GetLocation(),
                            "use a named type as argument"
                        )
                    );
                    continue;
                }
                baseTypes.Add(named);
            }

            if (context.SemanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol classType)
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

            return new(classType, baseTypes.ToImmutable(), diagnostics.ToImmutable());
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
            INamedTypeSymbol type,
            INamedTypeSymbol baseType
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
                .Where(m => !m.Modifiers.Any(t => t.IsKind(SyntaxKind.AbstractKeyword)))
                .ToArray();
            var baseList = baseTypeSyntax
                .BaseList;

            ReportMissingAbstractImplementations(spc, type, baseType);

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
            INamedTypeSymbol type,
            INamedTypeSymbol baseType,
            out SyntaxNode root,
            out TypeDeclarationSyntax baseTypeSyntax
        )
        {
            root = null!;
            baseTypeSyntax = null!;

            var baseTypeError = LoadSerializedBaseSyntax(
                baseType,
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
            INamedTypeSymbol Type,
            ImmutableArray<INamedTypeSymbol> BaseTypes,
            ImmutableArray<Diagnostic> Diagnostics
        );

        private static void ReportMissingAbstractImplementations(
            SourceProductionContext spc,
            INamedTypeSymbol type,
            INamedTypeSymbol baseType
        )
        {
            var symbolComparer = SymbolEqualityComparer.Default;
            var unimplemented = baseType
                .GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => m is { IsAbstract: true, MethodKind: MethodKind.Ordinary })
                .Where(abstractMethod =>
                    !type.GetMembers(abstractMethod.Name)
                        .OfType<IMethodSymbol>()
                        .Any(m => ParametersMatch(m, abstractMethod, symbolComparer))
                )
                .ToArray();

            if (unimplemented.Length == 0)
                return;

            // Use the original definition's doc id: constructed forms like
            // `T:DEnumerable{System.Int32}` aren't valid declaration ids and don't round-trip
            // through GetFirstSymbolForDeclarationId.
            var typeArgDocIds = string.Join(
                ";",
                baseType.TypeArguments.Select(t => t.GetDocumentationCommentId() ?? string.Empty)
            );
            var memberDocIds = string.Join(
                ";",
                unimplemented.Select(m => m.OriginalDefinition.GetDocumentationCommentId() ?? string.Empty)
            );
            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(DiagnosticProperties.BaseTypeDocId, baseType.OriginalDefinition.GetDocumentationCommentId() ?? string.Empty)
                .Add(DiagnosticProperties.BaseTypeName, baseType.Name)
                .Add(DiagnosticProperties.TypeArgDocIds, typeArgDocIds)
                .Add(DiagnosticProperties.MemberDocIds, memberDocIds);

            var memberList = string.Join(", ", unimplemented.Select(m => $"'{m.Name}'"));
            spc.ReportDiagnostic(
                Diagnostic.Create(
                    Descriptors.AbstractMemberNotImplemented,
                    type.Locations[0],
                    properties,
                    type.Name,
                    baseType.Name,
                    memberList
                )
            );
        }

        private static bool ParametersMatch(
            IMethodSymbol a,
            IMethodSymbol b,
            SymbolEqualityComparer comparer
        )
        {
            if (a.Parameters.Length != b.Parameters.Length)
                return false;
            for (int i = 0; i < a.Parameters.Length; i++)
            {
                if (!comparer.Equals(a.Parameters[i].Type, b.Parameters[i].Type))
                    return false;
            }
            return true;
        }

        private static TypeParameterSubstitutionRewriter? CreateTypeParameterSubstitution(
            INamedTypeSymbol baseType
        )
        {
            if (!baseType.IsGenericType)
                return null;

            var parameters = baseType.OriginalDefinition.TypeParameters;
            var arguments = baseType.TypeArguments;
            if (parameters.Length != arguments.Length)
            {
                throw new InvalidOperationException(
                    $"Type parameter/argument arity mismatch on '{baseType}': "
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
