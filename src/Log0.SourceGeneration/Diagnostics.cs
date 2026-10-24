using Microsoft.CodeAnalysis;

namespace Log0.SourceGeneration;

internal static class Diagnostics
{
    private const string CATEGORY = "Log0";

    public static readonly DiagnosticDescriptor UnsupportedShape = new(
        "LOG004",
        "[Log] method shape is not supported",
        "[Log] cannot wrap '{0}': {1} are not supported",
        CATEGORY,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor NotInterceptable = new(
        "LOG005",
        "[Log] method cannot be intercepted",
        "Calls to '{0}' cannot be logged: {1}",
        CATEGORY,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
}
