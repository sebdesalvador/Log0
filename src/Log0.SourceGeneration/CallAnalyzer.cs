using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Log0.SourceGeneration;

/// <summary>
/// Turns a call to a <c>[Log]</c> method into an interceptor: a generated method the compiler
/// calls instead, which logs around a call to the original.
/// </summary>
internal static class CallAnalyzer
{
    #region Constants

    private const string LOG_ATTRIBUTE = "Log0.LogAttribute";

    /// <summary>
    /// The namespace interceptors are declared in. The compiler only accepts interceptors from
    /// namespaces listed in the <c>InterceptorsNamespaces</c> property, which the package sets.
    /// </summary>
    public const string INTERCEPTORS_NAMESPACE = "Log0.Generated";

    private const string RECEIVER = "__log0_this";

    #endregion

    #region Public Methods

    /// <summary>Cheap syntactic filter: anything that looks like a call.</summary>
    public static bool IsCandidate( SyntaxNode node, CancellationToken _ ) => node is InvocationExpressionSyntax;

    /// <summary>The interceptor for this call, or null when it doesn't target a [Log] method.</summary>
    public static InterceptedCall? Analyze( GeneratorSyntaxContext context, CancellationToken cancellationToken )
    {
        var invocation = ( InvocationExpressionSyntax )context.Node;

        if ( context.SemanticModel.GetSymbolInfo( invocation, cancellationToken ).Symbol is not IMethodSymbol called )
            return null;

        // A reduced extension method (text.Shout()) is intercepted as the static method it is.
        var method = ( called.ReducedFrom ?? called ).OriginalDefinition;
        var attribute = FindAttribute( method );

        if ( attribute is null )
            return null;

        // base.M() must stay a non-virtual call; the interceptor could only make a virtual one.
        if ( invocation.Expression is MemberAccessExpressionSyntax { Expression: BaseExpressionSyntax } )
            return null;

        // Problems are reported once, on the method, by the declaration pipeline.
        if ( Check( method, cancellationToken ) is not null )
            return null;

        var location = context.SemanticModel.GetInterceptableLocation( invocation, cancellationToken );

        if ( location is null )
            return null;

        return new InterceptedCall( BuildInterceptor( method, attribute ), location.GetInterceptsLocationAttributeSyntax() );
    }

    /// <summary>Why calls to <paramref name="method"/> can't be intercepted, or null when they can.</summary>
    public static Problem? Check( IMethodSymbol method, CancellationToken cancellationToken )
    {
        if ( method.ReturnsByRef || method.ReturnsByRefReadonly )
            return new Problem( Diagnostics.UnsupportedShape, method.Name, "ref returns" );

        if ( MethodAnalyzer.IsIterator( method, cancellationToken ) )
            return new Problem( Diagnostics.UnsupportedShape, method.Name, "iterators" );

        if ( method.IsAsync && method.ReturnsVoid )
            return new Problem( Diagnostics.UnsupportedShape, method.Name, "async void methods" );

        if ( method.IsStatic && ( method.IsAbstract || method.IsVirtual ) )
            return new Problem( Diagnostics.NotInterceptable, method.Name, "static abstract and static virtual members are called through a type parameter" );

        if ( method.ContainingType.IsValueType )
            return new Problem( Diagnostics.NotInterceptable, method.Name, "struct methods are not supported" );

        for ( ISymbol symbol = method; symbol is INamedTypeSymbol or IMethodSymbol; symbol = symbol.ContainingSymbol )
        {
            if ( symbol.DeclaredAccessibility is not ( Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal ) )
                return new Problem( Diagnostics.NotInterceptable, method.Name, $"'{symbol.Name}' is not visible to generated code; make it internal or public" );
        }

        return null;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// The [Log] attribute on the method or on a method it overrides: a call bound to an override
    /// is logged like a call to the virtual method it overrides.
    /// </summary>
    private static AttributeData? FindAttribute( IMethodSymbol method )
    {
        for ( var current = method; current is not null; current = current.OverriddenMethod )
        {
            var attribute = current.GetAttributes().FirstOrDefault( a => a.AttributeClass?.ToDisplayString() == LOG_ATTRIBUTE );

            if ( attribute is not null )
                return attribute;
        }

        return null;
    }

    private static MethodModel BuildInterceptor( IMethodSymbol method, AttributeData attribute )
    {
        var (shape, resultType) = MethodAnalyzer.ClassifyReturn( method.ReturnType );

        // The interceptor declares the containing types' type parameters, then the method's;
        // the compiler fills them in from the call in that order.
        var typeParameters = ContainingTypes( method.ContainingType ).SelectMany( t => t.TypeParameters )
                                                                     .Concat( method.TypeParameters )
                                                                     .ToArray();

        var reserved = method.Parameters.Select( p => p.Name ).Concat( typeParameters.Select( p => p.Name ) ).ToArray();
        var receiver = RECEIVER;

        while ( reserved.Contains( receiver ) )
            receiver += "_";

        return new MethodModel(
            Type: BuildType( method.ContainingType ),
            Name: method.Name,
            DisplayName: MethodAnalyzer.DisplayName( method ),
            Level: MethodAnalyzer.ReadLevel( attribute ),
            Signature: BuildSignature( method, typeParameters, receiver ),
            Call: BuildCall( method, receiver ),
            Shape: shape,
            ResultType: resultType,
            Parameters: MethodAnalyzer.LoggedParameters( method ).ToArray(),
            ReservedNames: reserved,
            Attributes: new string[ 0 ]
        );
    }

    private static IEnumerable< INamedTypeSymbol > ContainingTypes( INamedTypeSymbol type )
    {
        var chain = new List< INamedTypeSymbol >();

        for ( var current = type; current is not null; current = current.ContainingType )
            chain.Insert( 0, current );

        return chain;
    }

    /// <summary>
    /// A static class under <see cref="INTERCEPTORS_NAMESPACE"/>, one per type whose methods are
    /// called, with its own logger for the type's category. Not file-local: that would mangle its
    /// name in stack traces.
    /// </summary>
    private static TypeModel BuildType( INamedTypeSymbol type )
    {
        var chain = ContainingTypes( type ).ToArray();
        var ns = type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString();
        var stem = ( ns is null ? "" : ns + "." ) + string.Join( ".", chain.Select( t => t.MetadataName ) );
        var className = string.Join( "_", chain.Select( t => t.MetadataName.Replace( '`', '_' ) ) ) + "_Log0Interceptors";

        // typeof( Outer<>.Inner<,> ): an unbound type has the same category name as ILogger<T>.
        var unbound = "global::" + ( ns is null ? "" : ns + "." )
                    + string.Join( ".", chain.Select( t => MethodAnalyzer.Escape( t.Name ) + ( t.Arity == 0 ? "" : "<" + new string( ',', t.Arity - 1 ) + ">" ) ) );

        return new TypeModel(
            Key: type.ToDisplayString( SymbolDisplayFormat.FullyQualifiedFormat ),
            HintName: stem.Replace( '`', '_' ) + ".Interceptors",
            // Mirrors the type's namespace, so same-named types can't clash and stack traces read well.
            Namespace: INTERCEPTORS_NAMESPACE + ( ns is null ? "" : "." + ns ),
            Containers: new[ ] { new ContainingType( $"internal static class {className}" ) },
            TypeOfName: unbound
        );
    }

    private static string BuildSignature( IMethodSymbol method, ITypeParameterSymbol[ ] typeParameters, string receiver )
    {
        var returnType = method.ReturnsVoid ? "void" : method.ReturnType.ToDisplayString( MethodAnalyzer.TypeFormat );
        var generics = typeParameters.Length == 0
            ? ""
            : "<" + string.Join( ", ", typeParameters.Select( p => MethodAnalyzer.Escape( p.Name ) ) ) + ">";

        var parameters = method.Parameters.Select( p => MethodAnalyzer.DeclareParameter( method, p ) );

        if ( !method.IsStatic )
            parameters = parameters.Prepend( $"this {method.ContainingType.ToDisplayString( MethodAnalyzer.TypeFormat )} {receiver}" );

        var constraints = string.Concat( typeParameters.Select( MethodAnalyzer.Constraints ) );
        var isUnsafe = method.Parameters.Any( p => IsPointer( p.Type ) ) || IsPointer( method.ReturnType );

        return $"internal static {( isUnsafe ? "unsafe " : "" )}{returnType} {MethodAnalyzer.Escape( method.Name )}{generics}{MethodAnalyzer.Parenthesize( string.Join( ", ", parameters ) )}{constraints}";
    }

    private static bool IsPointer( ITypeSymbol type )
        => type is IPointerTypeSymbol or IFunctionPointerTypeSymbol
        || type is IArrayTypeSymbol array && IsPointer( array.ElementType );

    /// <summary>The original call: virtual and interface calls still dispatch as they would have.</summary>
    private static string BuildCall( IMethodSymbol method, string receiver )
    {
        var target = method.IsStatic ? method.ContainingType.ToDisplayString( MethodAnalyzer.TypeFormat ) : receiver;
        var typeArguments = method.TypeParameters.Length == 0
            ? ""
            : "<" + string.Join( ", ", method.TypeParameters.Select( p => MethodAnalyzer.Escape( p.Name ) ) ) + ">";

        var arguments = method.Parameters.Select( p => p.RefKind switch
        {
            RefKind.Ref => "ref ",
            RefKind.Out => "out ",
            RefKind.In or RefKind.RefReadOnlyParameter => "in ",
            _ => ""
        } + MethodAnalyzer.Escape( p.Name ) );

        return $"{target}.{MethodAnalyzer.Escape( method.Name )}{typeArguments}{MethodAnalyzer.Parenthesize( string.Join( ", ", arguments ) )}";
    }

    #endregion
}

/// <summary>Why a method can't be intercepted, ready to become a diagnostic.</summary>
internal sealed class Problem( DiagnosticDescriptor descriptor, params object[ ] arguments )
{
    public DiagnosticDescriptor Descriptor { get; } = descriptor;

    public object[ ] Arguments { get; } = arguments;
}
