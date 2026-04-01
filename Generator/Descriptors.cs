using Microsoft.CodeAnalysis;

namespace Derive.Generator
{
    internal static class Descriptors
    {
        public static readonly DiagnosticDescriptor InvalidClassSignature = new(
            id: DeriveDiagnosticsConstants.InvalidClassSignatureId,
            title: "Invalid class signature",
            messageFormat: "The class should be marked as '{0}'",
            category: "Derive",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        public static readonly DiagnosticDescriptor InvalidDeriveArguments = new(
            id: DeriveDiagnosticsConstants.InvalidDeriveArgumentsId,
            title: "Invalid use of Derive attribute",
            messageFormat: "Derive attribute should '{0}'",
            category: "Derive",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        public static readonly DiagnosticDescriptor PublicBaseTypeNotAttributed = new(
            id: DeriveDiagnosticsConstants.PublicBaseTypeNotAttributedId,
            title: "Cannot derive from a public base type from another library",
            messageFormat: "Class '{0}' derives from {1} but the base type {2}",
            category: "Derive",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        public static readonly DiagnosticDescriptor PartialBaseType = new(
            id: DeriveDiagnosticsConstants.PartialBaseTypeId,
            title: "A base type cannot be partial",
            messageFormat: "Class '{0}' is a partial class, this is not supported",
            category: "Derive",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );
    }
}
