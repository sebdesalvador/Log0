using Microsoft.CodeAnalysis;

namespace Log0.SourceGeneration;

/// <summary>How the generated body waits for the implementation to finish.</summary>
internal enum ReturnShape
{
    Void,
    Value,
    Task,
    TaskOfT,
    ValueTask,
    ValueTaskOfT
}

/// <summary>A type declaration the generated methods are nested in, outermost first.</summary>
internal sealed record ContainingType( string Declaration );

/// <summary>A parameter whose value goes into the <c>Start</c> entry.</summary>
/// <param name="Name">The name used in the message and as the structured property.</param>
/// <param name="Expression">What the generated code passes: the parameter, or the redaction constant.</param>
internal sealed record LoggedParameter( string Name, string Expression );

/// <summary>Everything the emitter needs for the interceptor class of one type holding [Log] methods.</summary>
/// <param name="Key">Identifies the type across methods, for grouping.</param>
/// <param name="HintName">File name stem for the generated source.</param>
/// <param name="Namespace">The namespace, or null for the global namespace.</param>
/// <param name="Containers">The type and every type enclosing it, outermost first.</param>
/// <param name="TypeOfName">The type as written inside itself, for <c>typeof</c>.</param>
internal sealed record TypeModel(
    string Key,
    string HintName,
    string? Namespace,
    EquatableArray< ContainingType > Containers,
    string TypeOfName
);

/// <summary>Everything the emitter needs for the interceptor of one [Log] method.</summary>
/// <param name="Signature">The interceptor's header, without body.</param>
/// <param name="Call">The call to the original method, arguments included.</param>
/// <param name="ResultType">The returned type, or for a task its result type.</param>
/// <param name="ReservedNames">Parameter and type parameter names, which generated identifiers must not reuse.</param>
/// <param name="Attributes">The <c>[InterceptsLocation]</c> attributes, one per call site.</param>
internal sealed record MethodModel(
    TypeModel Type,
    string Name,
    string DisplayName,
    int Level,
    string Signature,
    string Call,
    ReturnShape Shape,
    string? ResultType,
    EquatableArray< LoggedParameter > Parameters,
    EquatableArray< string > ReservedNames,
    EquatableArray< string > Attributes
);

/// <summary>One call to a [Log] method, and the interceptor that replaces it.</summary>
/// <param name="Method">The interceptor; identical for every call to the same method.</param>
/// <param name="Location">The <c>[InterceptsLocation]</c> attribute pointing at the call.</param>
internal sealed record InterceptedCall( MethodModel Method, string Location );
