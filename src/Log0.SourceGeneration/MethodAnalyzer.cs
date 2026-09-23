using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Log0.SourceGeneration;

/// <summary>
/// Turns a method carrying <c>[Log]</c> into a <see cref="MethodModel"/>, or a diagnostic
/// explaining why it can't be wrapped.
/// </summary>
internal static class MethodAnalyzer
{
    #region Constants

    private const string NO_LOG_ATTRIBUTE = "Log0.NoLogAttribute";
    private const string LOGGER_INTERFACE = "Microsoft.Extensions.Logging.ILogger";
    private const string CORE_SUFFIX = "Core";
    private const int DEFAULT_LEVEL = 2; // LogLevel.Information

    #endregion

    #region Fields

    /// <summary>Fully qualified, with nullable annotations, so generated code needs no usings.</summary>
    private static readonly SymbolDisplayFormat _typeFormat = SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
        SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
    );

    #endregion

    #region Public Methods

    public static Result Analyze( GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken )
    {
        var declaration = ( MethodDeclarationSyntax )context.TargetNode;
        var method = ( IMethodSymbol )context.TargetSymbol;
        var location = declaration.Identifier.GetLocation();

        var isPartial = declaration.Modifiers.Any( SyntaxKind.PartialKeyword );
        var hasBody = declaration.Body is not null || declaration.ExpressionBody is not null;

        if ( !isPartial || hasBody || method.PartialImplementationPart is not null )
            return Fail( Diagnostics.MustBePartialDefinition, location, method.Name );

        for ( var type = method.ContainingType; type is not null; type = type.ContainingType )
        {
            if ( !IsPartial( type, cancellationToken ) )
                return Fail( Diagnostics.ContainingTypeMustBePartial, location, type.Name, method.Name );
        }

        if ( method.ReturnsByRef || method.ReturnsByRefReadonly )
            return Fail( Diagnostics.UnsupportedShape, location, method.Name, "ref returns" );

        var core = FindCore( method );

        if ( core is null )
            return Fail( Diagnostics.MissingCore, location, method.Name, method.Name + CORE_SUFFIX, method.ContainingType.Name );

        if ( IsIterator( core, cancellationToken ) )
            return Fail( Diagnostics.UnsupportedShape, location, method.Name, "iterators" );

        if ( core.IsAsync && core.ReturnsVoid )
            return Fail( Diagnostics.UnsupportedShape, location, method.Name, "async void methods" );

        var (shape, resultType) = ClassifyReturn( method.ReturnType );

        var model = new MethodModel(
            Type: BuildType( method.ContainingType ),
            Name: method.Name,
            DisplayName: DisplayName( method ),
            Level: ReadLevel( context.Attributes[ 0 ] ),
            Signature: BuildSignature( declaration, method ),
            CoreCall: BuildCoreCall( method ),
            Shape: shape,
            ResultType: resultType,
            LoggerExpression: FindLogger( method ),
            Parameters: LoggedParameters( method ).ToArray(),
            ReservedNames: method.Parameters.Select( p => p.Name ).Concat( method.TypeParameters.Select( p => p.Name ) ).ToArray()
        );

        return Result.Success( model );
    }

    #endregion

    #region Private Methods

    private static Result Fail( DiagnosticDescriptor descriptor, Location location, params object[ ] arguments )
        => Result.Failure( Diagnostic.Create( descriptor, location, arguments ) );

    private static bool IsPartial( INamedTypeSymbol type, CancellationToken cancellationToken )
        => type.DeclaringSyntaxReferences
               .Select( r => r.GetSyntax( cancellationToken ) )
               .OfType< TypeDeclarationSyntax >()
               .Any( t => t.Modifiers.Any( SyntaxKind.PartialKeyword ) );

    /// <summary>
    /// The user's implementation: same name plus <c>Core</c>, same staticness, and the same
    /// parameter types and ref kinds. Failing an exact match, the first with the same parameter
    /// count; the compiler's error on the generated call then explains the mismatch.
    /// </summary>
    private static IMethodSymbol? FindCore( IMethodSymbol method )
    {
        var candidates = method.ContainingType
                               .GetMembers( method.Name + CORE_SUFFIX )
                               .OfType< IMethodSymbol >()
                               .Where( m => m.Arity == method.Arity
                                         && m.Parameters.Length == method.Parameters.Length
                                         && m.IsStatic == method.IsStatic )
                               .ToArray();

        return candidates.FirstOrDefault( c => SameParameters( method, c ) ) ?? candidates.FirstOrDefault();
    }

    /// <summary>Compares parameters, lining up each method's own type parameters by position.</summary>
    private static bool SameParameters( IMethodSymbol method, IMethodSymbol candidate )
    {
        var aligned = candidate.Arity == 0 ? candidate : candidate.Construct( method.TypeParameters.ToArray< ITypeSymbol >() );

        return method.Parameters
                     .Zip( aligned.Parameters, ( a, b ) => a.RefKind == b.RefKind
                                                        && SymbolEqualityComparer.Default.Equals( a.Type, b.Type ) )
                     .All( same => same );
    }

    /// <summary>
    /// Whether the body yields. Logging around an iterator would report <c>Success</c> before any
    /// of it has run, so iterators are rejected. Yields inside lambdas and local functions don't count.
    /// </summary>
    private static bool IsIterator( IMethodSymbol method, CancellationToken cancellationToken )
        => method.DeclaringSyntaxReferences
                 .Select( r => r.GetSyntax( cancellationToken ) )
                 .Any( body => body.DescendantNodes( n => n == body || n is not ( LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax ) )
                                   .OfType< YieldStatementSyntax >()
                                   .Any() );

    private static int ReadLevel( AttributeData attribute )
        => attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[ 0 ].Value is int level ? level : DEFAULT_LEVEL;

    private static (ReturnShape Shape, string? ResultType) ClassifyReturn( ITypeSymbol returnType )
    {
        if ( returnType.SpecialType == SpecialType.System_Void )
            return (ReturnShape.Void, null);

        if ( returnType is INamedTypeSymbol { ContainingNamespace: { } ns } named
          && ns.ToDisplayString() == "System.Threading.Tasks" )
        {
            var result = named.TypeArguments.Length == 1 ? named.TypeArguments[ 0 ].ToDisplayString( _typeFormat ) : null;

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

        return (ReturnShape.Value, returnType.ToDisplayString( _typeFormat ));
    }

    private static TypeModel BuildType( INamedTypeSymbol type )
    {
        var chain = new List< INamedTypeSymbol >();

        for ( var current = type; current is not null; current = current.ContainingType )
            chain.Insert( 0, current );

        var ns = type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString();
        var hint = ( ns is null ? "" : ns + "." ) + string.Join( ".", chain.Select( t => t.MetadataName ) );

        return new TypeModel(
            Key: type.ToDisplayString( SymbolDisplayFormat.FullyQualifiedFormat ),
            HintName: hint.Replace( '`', '_' ),
            Namespace: ns,
            Containers: chain.Select( t => new ContainingType( Declare( t ) ) ).ToArray(),
            TypeOfName: type.ToDisplayString( SymbolDisplayFormat.FullyQualifiedFormat )
        );
    }

    /// <summary>A partial re-declaration: <c>partial record struct Point&lt;T&gt;</c>.</summary>
    private static string Declare( INamedTypeSymbol type )
    {
        var keyword = type switch
        {
            { IsRecord: true, TypeKind: TypeKind.Struct } => "record struct",
            { IsRecord: true } => "record",
            { TypeKind: TypeKind.Struct } => "struct",
            { TypeKind: TypeKind.Interface } => "interface",
            _ => "class"
        };

        var typeParameters = type.TypeParameters.Length == 0
            ? ""
            : "<" + string.Join( ", ", type.TypeParameters.Select( p => Escape( p.Name ) ) ) + ">";

        return $"partial {keyword} {Escape( type.Name )}{typeParameters}";
    }

    /// <summary><c>OrderService.GetOrder</c>: enclosing types and method, no namespace, no type arguments.</summary>
    private static string DisplayName( IMethodSymbol method )
    {
        var parts = new List< string > { method.Name };

        for ( var type = method.ContainingType; type is not null; type = type.ContainingType )
            parts.Insert( 0, type.Name );

        return string.Join( ".", parts );
    }

    private static string BuildSignature( MethodDeclarationSyntax declaration, IMethodSymbol method )
    {
        // Modifiers are keywords, so copying the tokens is safe; they must match the definition.
        var modifiers = declaration.Modifiers
                                   .Where( m => !m.IsKind( SyntaxKind.PartialKeyword ) )
                                   .Select( m => m.Text )
                                   .Concat( [ "partial" ] );

        var returnType = method.ReturnsVoid ? "void" : method.ReturnType.ToDisplayString( _typeFormat );
        var typeParameters = method.TypeParameters.Length == 0
            ? ""
            : "<" + string.Join( ", ", method.TypeParameters.Select( p => Escape( p.Name ) ) ) + ">";
        var parameters = string.Join( ", ", method.Parameters.Select( p => DeclareParameter( method, p ) ) );
        var constraints = string.Concat( method.TypeParameters.Select( Constraints ) );

        return $"{string.Join( " ", modifiers )} {returnType} {Escape( method.Name )}{typeParameters}{Parenthesize( parameters )}{constraints}";
    }

    private static string DeclareParameter( IMethodSymbol method, IParameterSymbol parameter )
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

        return prefix + parameter.Type.ToDisplayString( _typeFormat ) + " " + Escape( parameter.Name );
    }

    /// <summary>A partial implementation must repeat its definition's constraints.</summary>
    private static string Constraints( ITypeParameterSymbol parameter )
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

        clauses.AddRange( parameter.ConstraintTypes.Select( t => t.ToDisplayString( _typeFormat ) ) );

        if ( parameter.HasConstructorConstraint )
            clauses.Add( "new()" );

        if ( parameter.AllowsRefLikeType )
            clauses.Add( "allows ref struct" );

        return clauses.Count == 0 ? "" : $" where {Escape( parameter.Name )} : {string.Join( ", ", clauses )}";
    }

    private static string BuildCoreCall( IMethodSymbol method )
    {
        var typeArguments = method.TypeParameters.Length == 0
            ? ""
            : "<" + string.Join( ", ", method.TypeParameters.Select( p => Escape( p.Name ) ) ) + ">";

        var arguments = method.Parameters.Select( p => p.RefKind switch
        {
            RefKind.Ref => "ref ",
            RefKind.Out => "out ",
            RefKind.In or RefKind.RefReadOnlyParameter => "in ",
            _ => ""
        } + Escape( p.Name ) );

        return $"{Escape( method.Name + CORE_SUFFIX )}{typeArguments}{Parenthesize( string.Join( ", ", arguments ) )}";
    }

    /// <summary>
    /// An <c>ILogger</c> field or property the class already has, so tests and apps that inject
    /// one per instance see the logs there. Null means use the shared factory.
    /// </summary>
    private static string? FindLogger( IMethodSymbol method )
    {
        for ( var type = method.ContainingType; type is not null; type = type.BaseType )
        {
            var declaredHere = SymbolEqualityComparer.Default.Equals( type, method.ContainingType );

            foreach ( var member in type.GetMembers() )
            {
                if ( member.IsImplicitlyDeclared || ( !declaredHere && member.DeclaredAccessibility == Accessibility.Private ) )
                    continue;

                if ( method.IsStatic && !member.IsStatic )
                    continue;

                var memberType = member switch
                {
                    IFieldSymbol field => field.Type,
                    IPropertySymbol { GetMethod: not null, IsIndexer: false } property => property.Type,
                    _ => null
                };

                if ( memberType is null || !IsLogger( memberType ) )
                    continue;

                return member.IsStatic
                    ? $"{type.ToDisplayString( SymbolDisplayFormat.FullyQualifiedFormat )}.{Escape( member.Name )}"
                    : $"this.{Escape( member.Name )}";
            }
        }

        return null;
    }

    private static bool IsLogger( ITypeSymbol type )
        => type.ToDisplayString() == LOGGER_INTERFACE
        || type.AllInterfaces.Any( i => i.ToDisplayString() == LOGGER_INTERFACE );

    private static IEnumerable< LoggedParameter > LoggedParameters( IMethodSymbol method )
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

    private static string Parenthesize( string list ) => list.Length == 0 ? "()" : $"( {list} )";

    /// <summary>A parameter called <c>@class</c> must be written with its <c>@</c>.</summary>
    private static string Escape( string name )
        => SyntaxFacts.GetKeywordKind( name ) != SyntaxKind.None ? "@" + name : name;

    #endregion
}
