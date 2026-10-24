using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Log0.SourceGeneration;

/// <summary>
/// Logs <c>Start</c>, then <c>Success</c>, <c>Canceled</c> or <c>Error</c> around methods marked
/// <c>[Log]</c>. A partial method gets a generated body that calls its <c>Core</c> implementation;
/// a plain method keeps its body and every call to it is intercepted instead.
/// </summary>
[ Generator ]
public sealed class LogGenerator : IIncrementalGenerator
{
    private const string LOG_ATTRIBUTE = "Log0.LogAttribute";

    public void Initialize( IncrementalGeneratorInitializationContext context )
    {
        var results = context.SyntaxProvider.ForAttributeWithMetadataName(
            LOG_ATTRIBUTE,
            static ( node, _ ) => node is Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax,
            MethodAnalyzer.Analyze
        );

        context.RegisterSourceOutput(
            results.Where( static r => r.Diagnostic is not null ),
            static ( spc, result ) => spc.ReportDiagnostic( result.Diagnostic! )
        );

        // One file per type, since the type's shared logger field must be declared once.
        var models = results.Where( static r => r.Model is not null )
                            .Select( static ( r, _ ) => r.Model! )
                            .Collect();

        context.RegisterSourceOutput( models, Emit );

        var calls = context.SyntaxProvider.CreateSyntaxProvider( CallAnalyzer.IsCandidate, CallAnalyzer.Analyze )
                                          .Where( static c => c is not null )
                                          .Select( static ( c, _ ) => c! )
                                          .Collect();

        context.RegisterSourceOutput( calls, EmitInterceptors );
    }

    private static void Emit( SourceProductionContext context, ImmutableArray< MethodModel > methods )
    {
        foreach ( var group in methods.GroupBy( m => m.Type.Key ) )
        {
            var type = group.First().Type;
            context.AddSource( $"{type.HintName}.Log0.g.cs", LogEmitter.Emit( type, group.ToArray() ) );
        }
    }

    /// <summary>One interceptor per called method, carrying one attribute per call site.</summary>
    private static void EmitInterceptors( SourceProductionContext context, ImmutableArray< InterceptedCall > calls )
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
