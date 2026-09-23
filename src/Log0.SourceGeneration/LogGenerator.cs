using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Log0.SourceGeneration;

/// <summary>
/// Writes the body of every partial method marked <c>[Log]</c>: log <c>Start</c>, call the
/// <c>Core</c> implementation, then log <c>Success</c>, <c>Canceled</c> or <c>Error</c>.
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
    }

    private static void Emit( SourceProductionContext context, ImmutableArray< MethodModel > methods )
    {
        foreach ( var group in methods.GroupBy( m => m.Type.Key ) )
        {
            var type = group.First().Type;
            context.AddSource( $"{type.HintName}.Log0.g.cs", LogEmitter.Emit( type, group.ToArray() ) );
        }
    }
}
