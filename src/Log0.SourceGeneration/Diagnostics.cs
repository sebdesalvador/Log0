using Microsoft.CodeAnalysis;

namespace Log0.SourceGeneration;

internal static class Diagnostics
{
    private const string CATEGORY = "Log0";

    public static readonly DiagnosticDescriptor MustBePartialDefinition = new(
        "LOG001",
        "[Log] method must be a partial method without a body",
        "[Log] method '{0}' must be declared partial, without a body. Write the body in '{0}Core' instead.",
        CATEGORY,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor MissingCore = new(
        "LOG002",
        "[Log] method has no Core implementation",
        "[Log] method '{0}' has no implementation. Add a method '{1}' to '{2}' with the same parameters and return type.",
        CATEGORY,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor ContainingTypeMustBePartial = new(
        "LOG003",
        "Type containing a [Log] method must be partial",
        "Type '{0}' must be declared partial, because it contains the [Log] method '{1}'",
        CATEGORY,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor UnsupportedShape = new(
        "LOG004",
        "[Log] method shape is not supported",
        "[Log] cannot wrap '{0}': {1} are not supported",
        CATEGORY,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
}
