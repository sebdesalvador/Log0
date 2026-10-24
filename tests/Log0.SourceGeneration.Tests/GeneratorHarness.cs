using System.Collections.Immutable;
using Log0.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Log0.SourceGeneration.Tests;

/// <summary>Compiles a snippet with the generator and reports what came out.</summary>
internal static class GeneratorHarness
{
    #region Fields

    private static readonly ImmutableArray< MetadataReference > _references =
    [
        .. ( ( string )AppContext.GetData( "TRUSTED_PLATFORM_ASSEMBLIES" )! )
           .Split( Path.PathSeparator )
           .Select( path => MetadataReference.CreateFromFile( path ) ),
        MetadataReference.CreateFromFile( typeof( LogAttribute ).Assembly.Location ),
        MetadataReference.CreateFromFile( typeof( Microsoft.Extensions.Logging.ILogger ).Assembly.Location )
    ];

    /// <summary>What the package's props set: interceptors are only accepted from this namespace.</summary>
    private static readonly CSharpParseOptions _parseOptions = new CSharpParseOptions( LanguageVersion.Preview )
        .WithFeatures( [ new KeyValuePair< string, string >( "InterceptorsNamespaces", "Log0.Generated" ) ] );

    #endregion

    #region Public Methods

    /// <summary>
    /// Runs the generator over <paramref name="source"/>. Returns the generator's own diagnostics,
    /// the errors and warnings of the compilation including generated code, and the generated sources.
    /// </summary>
    public static GeneratorRun Run( string source, NullableContextOptions nullable = NullableContextOptions.Enable )
    {
        var compilation = CSharpCompilation.Create(
            "Snippet",
            [ CSharpSyntaxTree.ParseText( source, _parseOptions, path: "Snippet.cs" ) ],
            _references,
            new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: nullable, allowUnsafe: true )
        );

        GeneratorDriver driver = CSharpGeneratorDriver.Create( new LogGenerator() )
                                                      .WithUpdatedParseOptions( _parseOptions );
        driver = driver.RunGeneratorsAndUpdateCompilation( compilation, out var output, out var generatorDiagnostics );

        var compilerDiagnostics = output.GetDiagnostics()
                                        .Where( d => d.Severity >= DiagnosticSeverity.Warning )
                                        .ToArray();

        var generated = driver.GetRunResult().GeneratedTrees.Select( t => t.ToString() ).ToArray();

        return new GeneratorRun( generatorDiagnostics, compilerDiagnostics, generated );
    }

    #endregion
}

internal sealed record GeneratorRun(
    ImmutableArray< Diagnostic > GeneratorDiagnostics,
    IReadOnlyList< Diagnostic > CompilerDiagnostics,
    IReadOnlyList< string > Generated
)
{
    public string Describe()
        => string.Join( "\n", CompilerDiagnostics.Select( d => d.ToString() ) ) + "\n---\n" + string.Join( "\n", Generated );
}
