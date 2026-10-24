using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Log0.SourceGeneration;

/// <summary>
/// Checks each <c>[Log]</c> declaration once, and reads from a method what its interceptor needs:
/// return shape, display name, signature pieces and the parameters to log.
/// </summary>
internal static class MethodAnalyzer
{
    #region Constants

    private const string NO_LOG_ATTRIBUTE = "Log0.NoLogAttribute";
    private const int DEFAULT_LEVEL = 2; // LogLevel.Information

    #endregion

    #region Fields

    /// <summary>Fully qualified, with nullable annotations, so generated code needs no usings.</summary>
    internal static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
        SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
    );

    #endregion

    #region Public Methods

    /// <summary>Reports a [Log] method whose calls can't be intercepted; nothing else to do here.</summary>
    public static Diagnostic? Analyze( GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken )
    {
        var declaration = ( MethodDeclarationSyntax )context.TargetNode;
        var method = ( IMethodSymbol )context.TargetSymbol;

        return CallAnalyzer.Check( method, cancellationToken ) is { } problem
            ? Diagnostic.Create( problem.Descriptor, declaration.Identifier.GetLocation(), problem.Arguments )
            : null;
    }

    #endregion

    #region Internal Methods

    /// <summary>
    /// Whether the body yields. Logging around an iterator would report <c>Success</c> before any
    /// of it has run, so iterators are rejected. Yields inside lambdas and local functions don't count.
    /// </summary>
    internal static bool IsIterator( IMethodSymbol method, CancellationToken cancellationToken )
        => method.DeclaringSyntaxReferences
                 .Select( r => r.GetSyntax( cancellationToken ) )
                 .Any( body => body.DescendantNodes( n => n == body || n is not ( LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax ) )
                                   .OfType< YieldStatementSyntax >()
                                   .Any() );

    internal static int ReadLevel( AttributeData attribute )
        => attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[ 0 ].Value is int level ? level : DEFAULT_LEVEL;

    internal static (ReturnShape Shape, string? ResultType) ClassifyReturn( ITypeSymbol returnType )
    {
        if ( returnType.SpecialType == SpecialType.System_Void )
            return (ReturnShape.Void, null);

        if ( returnType is INamedTypeSymbol { ContainingNamespace: { } ns } named
          && ns.ToDisplayString() == "System.Threading.Tasks" )
        {
            var result = named.TypeArguments.Length == 1 ? named.TypeArguments[ 0 ].ToDisplayString( TypeFormat ) : null;

            switch ( named.MetadataName )
            {
                case "Task":
                    return (ReturnShape.Task, null);
                case "Task`1":
                    return (ReturnShape.TaskOfT, result);
                case "ValueTask":
                    return (ReturnShape.ValueTask, null);
                case "ValueTask`1":
                    return (ReturnShape.ValueTaskOfT, result);
            }
        }

        return (ReturnShape.Value, returnType.ToDisplayString( TypeFormat ));
    }

    /// <summary><c>OrderService.GetOrder</c>: enclosing types and method, no namespace, no type arguments.</summary>
    internal static string DisplayName( IMethodSymbol method )
    {
        var parts = new List< string > { method.Name };

        for ( var type = method.ContainingType; type is not null; type = type.ContainingType )
            parts.Insert( 0, type.Name );

        return string.Join( ".", parts );
    }

    internal static string DeclareParameter( IMethodSymbol method, IParameterSymbol parameter )
    {
        var prefix = "";

        if ( method.IsExtensionMethod && parameter.Ordinal == 0 )
            prefix += "this ";

        if ( parameter.ScopedKind != ScopedKind.None )
            prefix += "scoped ";

        prefix += parameter.RefKind switch
        {
            RefKind.Ref => "ref ",
            RefKind.Out => "out ",
            RefKind.In => "in ",
            RefKind.RefReadOnlyParameter => "ref readonly ",
            _ => ""
        };

        if ( parameter.IsParams )
            prefix += "params ";

        return prefix + parameter.Type.ToDisplayString( TypeFormat ) + " " + Escape( parameter.Name );
    }

    /// <summary>An interceptor must repeat the constraints of the type parameters it declares.</summary>
    internal static string Constraints( ITypeParameterSymbol parameter )
    {
        var clauses = new List< string >();

        if ( parameter.HasReferenceTypeConstraint )
            clauses.Add( parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class" );

        if ( parameter.HasUnmanagedTypeConstraint )
            clauses.Add( "unmanaged" );
        else if ( parameter.HasValueTypeConstraint )
            clauses.Add( "struct" );

        if ( parameter.HasNotNullConstraint )
            clauses.Add( "notnull" );

        clauses.AddRange( parameter.ConstraintTypes.Select( t => t.ToDisplayString( TypeFormat ) ) );

        if ( parameter.HasConstructorConstraint )
            clauses.Add( "new()" );

        if ( parameter.AllowsRefLikeType )
            clauses.Add( "allows ref struct" );

        return clauses.Count == 0 ? "" : $" where {Escape( parameter.Name )} : {string.Join( ", ", clauses )}";
    }

    internal static IEnumerable< LoggedParameter > LoggedParameters( IMethodSymbol method )
    {
        if ( HasNoLog( method ) )
            yield break;

        foreach ( var parameter in method.Parameters )
        {
            if ( !CanLog( parameter ) )
                continue;

            var masked = HasNoLog( parameter ) || IsNoLogType( parameter.Type );

            yield return new LoggedParameter(
                parameter.Name,
                masked ? "global::Log0.Log0Method.Redacted" : Escape( parameter.Name )
            );
        }
    }

    /// <summary>
    /// Out parameters have no value yet, cancellation tokens are noise, and ref structs and
    /// pointers cannot be boxed.
    /// </summary>
    private static bool CanLog( IParameterSymbol parameter )
        => parameter.RefKind != RefKind.Out
        && parameter.Type is not ( IPointerTypeSymbol or IFunctionPointerTypeSymbol )
        && !parameter.Type.IsRefLikeType
        && parameter.Type is not ITypeParameterSymbol { AllowsRefLikeType: true }
        && parameter.Type.ToDisplayString() != "System.Threading.CancellationToken";

    private static bool HasNoLog( ISymbol symbol )
        => symbol.GetAttributes().Any( a => a.AttributeClass?.ToDisplayString() == NO_LOG_ATTRIBUTE );

    /// <summary>
    /// Whether the type, or anything it derives from or implements, is marked <c>[NoLog]</c>.
    /// Looks through nullable value types and arrays, so <c>ApiKey?</c> and <c>ApiKey[]</c> are masked too.
    /// </summary>
    private static bool IsNoLogType( ITypeSymbol type )
    {
        switch ( type )
        {
            case IArrayTypeSymbol array:
                return IsNoLogType( array.ElementType );
            case INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable:
                return IsNoLogType( nullable.TypeArguments[ 0 ] );
        }

        for ( var current = type; current is not null; current = current.BaseType )
        {
            if ( HasNoLog( current ) )
                return true;
        }

        return type.AllInterfaces.Any( HasNoLog );
    }

    internal static string Parenthesize( string list ) => list.Length == 0 ? "()" : $"( {list} )";

    /// <summary>A parameter called <c>@class</c> must be written with its <c>@</c>.</summary>
    internal static string Escape( string name )
        => SyntaxFacts.GetKeywordKind( name ) != SyntaxKind.None ? "@" + name : name;

    #endregion
}
