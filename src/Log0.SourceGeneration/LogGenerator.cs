using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Log0.SourceGeneration;

/// <summary>
/// Logs <c>Start</c>, then <c>Success</c>, <c>Canceled</c> or <c>Error</c> around every call to a
/// method marked <c>[Log]</c>. The method keeps its body; each call to it is intercepted.
/// </summary>
[ Generator ]
public sealed class LogGenerator : IIncrementalGenerator
{
    private const string LOG_ATTRIBUTE = "Log0.LogAttribute";

    public void Initialize( IncrementalGeneratorInitializationContext context )
    {
        // Problems with a method are reported once, on its declaration.
        var problems = context.SyntaxProvider.ForAttributeWithMetadataName(
            LOG_ATTRIBUTE,
            static ( node, _ ) => node is Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax,
            MethodAnalyzer.Analyze
        );

        context.RegisterSourceOutput(
            problems.Where( static d => d is not null ),
            static ( spc, diagnostic ) => spc.ReportDiagnostic( diagnostic! )
        );

        var calls = context.SyntaxProvider.CreateSyntaxProvider( CallAnalyzer.IsCandidate, CallAnalyzer.Analyze )
                                          .Where( static c => c is not null )
                                          .Select( static ( c, _ ) => c! )
                                          .Collect();

        context.RegisterSourceOutput( calls, Emit );
    }

    /// <summary>
    /// One file per type whose methods are called, since the type's logger field is declared
    /// once; one interceptor per method, carrying one attribute per call site.
    /// </summary>
    private static void Emit( SourceProductionContext context, ImmutableArray< InterceptedCall > calls )
    {
        foreach ( var group in calls.GroupBy( c => c.Method.Type.Key ) )
        {
            var interceptors = group.GroupBy( c => c.Method )
                                    .Select( m => m.Key with { Attributes = m.Select( c => c.Location ).Distinct().ToArray() } )
                                    .ToArray();

            var type = interceptors[ 0 ].Type;
            context.AddSource( $"{type.HintName}.Log0.g.cs", LogEmitter.Emit( type, interceptors ) );
        }
    }
}
