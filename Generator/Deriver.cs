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

            if (context.SemanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol classType)
                return null;

            var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
            var baseTypesBuilder = ImmutableArray.CreateBuilder<BaseTypeInfo>();

            // Get all DeriveAttribute instances from the symbol
            var deriveAttrs = classType.GetAttributes()
                .Where(a => a.AttributeClass?.Name == "DeriveAttribute")
                .ToArray();

            if (deriveAttrs.Length == 0)
                return null;

            foreach (var attr in deriveAttrs)
            {
                INamedTypeSymbol baseType;

                if (attr.AttributeClass?.IsGenericType == true)
                {
                    // [Derive<T>] form — type from generic attribute argument
                    if (attr.AttributeClass.TypeArguments.Length != 1)
                    {
                        diagnostics.Add(Diagnostic.Create(Descriptors.InvalidDeriveArguments, classDecl.GetLocation(), "generic Derive attribute must have exactly one type argument"));
                        continue;
                    }
                    if (attr.AttributeClass.TypeArguments[0] is not INamedTypeSymbol genericType)
                    {
                        diagnostics.Add(Diagnostic.Create(Descriptors.InvalidDeriveArguments, classDecl.GetLocation(), "Derive type argument must be a named type"));
                        continue;
                    }
                    baseType = genericType;
                }
                else
                {
                    // [Derive(typeof(T))] form — type from constructor argument
                    if (attr.ConstructorArguments.Length != 1)
                    {
                        diagnostics.Add(Diagnostic.Create(Descriptors.InvalidDeriveArguments, classDecl.GetLocation(), "Derive attribute must have a single Type argument"));
                        continue;
                    }
                    if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol ctorType)
                    {
                        diagnostics.Add(Diagnostic.Create(Descriptors.InvalidDeriveArguments, classDecl.GetLocation(), "Derive constructor argument must be a type"));
                        continue;
                    }
                    baseType = ctorType;
                }

                // Extract TypeParams from named arguments (both generic and non-generic forms can have this)
                var typeParamsBuilder = ImmutableArray.CreateBuilder<string>();
                foreach (var namedArg in attr.NamedArguments)
                {
                    if (namedArg.Key == "TypeParams")
                    {
                        foreach (var val in namedArg.Value.Values)
                        {
                            if (val.Value is string strVal)
                                typeParamsBuilder.Add(strVal);
                        }
                    }
                    else
                    {
                        diagnostics.Add(Diagnostic.Create(Descriptors.InvalidDeriveArguments, classDecl.GetLocation(), $"unknown named argument '{namedArg.Key}'"));
                        continue;
                    }
                }

                baseTypesBuilder.Add(new BaseTypeInfo(baseType, typeParamsBuilder.ToImmutable()));
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

            var derivedClass = new DerivedClassInfo(classType, classDecl.BaseList);
            return new(derivedClass, baseTypesBuilder.ToImmutable(), diagnostics.ToImmutable());
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
            foreach (var baseTypeInfo in classDeriveInfo.BaseTypes)
            {
                GenerateForBase(spc, classDeriveInfo.Class, baseTypeInfo);
            }
        }

        private static void GenerateForBase(
            SourceProductionContext spc,
            DerivedClassInfo derivedClass,
            BaseTypeInfo baseTypeInfo
        )
        {
            var type = derivedClass.Type;
            var userProvidedBases = derivedClass.UserProvidedBases;
            var baseType = baseTypeInfo.Type;

            // Handle unbound generics: construct by mapping parameter names (e.g., DEnumerable<> with typeParams: "T" → DEnumerable<T>)
            if (baseType.IsGenericType && baseType.TypeArguments.Length == 0)
            {
                var typeParams = baseTypeInfo.TypeParams;
                if (typeParams.Length != baseType.OriginalDefinition.TypeParameters.Length)
                    return;

                var baseConstructArgs = new ITypeSymbol[typeParams.Length];
                for (int i = 0; i < typeParams.Length; i++)
                {
                    var paramName = typeParams[i];
                    var typeParam = type.TypeParameters.FirstOrDefault(tp => tp.Name == paramName);
                    if (typeParam == null)
                        return; // TODO: add diagnostic for missing type parameter
                    baseConstructArgs[i] = typeParam;
                }

                baseType = baseType.Construct(baseConstructArgs);
            }

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
            var virtualOverriddenSpans = new HashSet<TextSpan>(
                baseType.GetMembers()
                    .Where(m => m.IsVirtual && type.HasCompatibleMember(m))
                    .SelectMany(m => m.DeclaringSyntaxReferences)
                    .Select(r => r.Span));
            var membersToCopy = baseTypeSyntax
                .Members
                .Where(m => m is MethodDeclarationSyntax or PropertyDeclarationSyntax)
                .Where(m => !m.Modifiers.Any(t => t.IsKind(SyntaxKind.AbstractKeyword)))
                .Where(m => !virtualOverriddenSpans.Contains(m.Span))
                .ToArray();
            var baseList = baseTypeSyntax
                .BaseList;

            ReportMissingAbstractImplementations(spc, type, baseType);

            if (membersToCopy.Length == 0 && baseList == null)
                return;

            var typeArgRewriter = CreateTypeParameterSubstitution(baseType);

            var builder = new IndentedStringBuilder();
            var baseNamespace = baseType.ContainingNamespace?.ToDisplayString();
            var usingLines = usingsToCopy.Select(u => u.ToString()).ToList();
            // Base types reference their own namespace implicitly; the generated file does not
            if (baseNamespace != null && !usingLines.Any(u => u.Contains(baseNamespace)))
                usingLines.Add($"using {baseNamespace};");
            if (usingLines.Count > 0)
            {
                foreach (var @using in usingLines)
                {
                    builder.AppendLine(@using);
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
                    var parts = new List<string>();

                    // Add user's base class (if it's not an interface)
                    if (type.BaseType is not null and not { TypeKind: TypeKind.Interface })
                    {
                        parts.Add(type.BaseType.ToDisplayString());
                    }

                    // Add Derive base's interfaces
                    if (baseList?.Types is not null)
                    {
                        parts.AddRange(
                            baseList.Types.Select(bt => (typeArgRewriter?.Visit(bt) ?? bt).ToString())
                        );
                    }

                    if (parts.Count > 0)
                    {
                        builder.Append(" : ");
                        builder.Append(string.Join(", ", parts));
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

        private sealed record BaseTypeInfo(
            INamedTypeSymbol Type,
            ImmutableArray<string> TypeParams
        );

        private sealed record DerivedClassInfo(
            INamedTypeSymbol Type,
            BaseListSyntax? UserProvidedBases
        );

        private sealed record ClassDeriveInfo(
            DerivedClassInfo Class,
            ImmutableArray<BaseTypeInfo> BaseTypes,
            ImmutableArray<Diagnostic> Diagnostics
        );

        private static void ReportMissingAbstractImplementations(
            SourceProductionContext spc,
            INamedTypeSymbol type,
            INamedTypeSymbol baseType
        )
        {
            var unimplemented = baseType
                .GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => m is { IsAbstract: true, MethodKind: MethodKind.Ordinary })
                .Where(abstractMethod => !type.HasCompatibleMember(abstractMethod))
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
