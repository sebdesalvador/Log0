using Xunit;

namespace Log0.SourceGeneration.Tests;

public sealed class DiagnosticTests
{
    #region Public Methods

    [ Theory ]
    [ InlineData( "[Log] public int Add( int a ) => a;" ) ]
    [ InlineData( "[Log] public partial int Add( int a ); public partial int Add( int a ) => a;" ) ]
    public void LOG001_when_the_method_is_not_a_bodiless_partial( string member )
        => AssertSingle( "LOG001", $"public partial class C {{ {member} private int AddCore( int a ) => a; }}" );

    [ Fact ]
    public void LOG002_when_there_is_no_Core_implementation()
    {
        var diagnostic = AssertSingle( "LOG002", "public partial class C { [Log] public partial int Add( int a ); }" );

        Assert.Contains( "AddCore", diagnostic.GetMessage() );
    }

    [ Fact ]
    public void LOG003_when_an_enclosing_type_is_not_partial()
        => AssertSingle(
            "LOG003",
            "public class Outer { public partial class C { [Log] public partial int Add( int a ); private int AddCore( int a ) => a; } }"
        );

    [ Theory ]
    [ InlineData( "[Log] public partial ref int Get(); private int _x; private ref int GetCore() => ref _x;", "ref returns" ) ]
    [ InlineData( "[Log] public partial IEnumerable<int> Get(); private IEnumerable<int> GetCore() { yield return 1; }", "iterators" ) ]
    [ InlineData( "[Log] public partial void Go(); private async void GoCore() => await Task.Yield();", "async void" ) ]
    public void LOG004_for_unsupported_shapes( string members, string reason )
    {
        var diagnostic = AssertSingle( "LOG004", $"public partial class C {{ {members} }}" );

        Assert.Contains( reason, diagnostic.GetMessage() );
    }

    [ Fact ]
    public void Yield_inside_a_lambda_does_not_make_an_iterator()
    {
        var run = GeneratorHarness.Run( Wrap(
            "public partial class C { [Log] public partial Func<IEnumerable<int>> Get(); private Func<IEnumerable<int>> GetCore() { IEnumerable<int> Local() { yield return 1; } return Local; } }"
        ) );

        Assert.Empty( run.GeneratorDiagnostics );
        Assert.Empty( run.CompilerDiagnostics );
    }

    #endregion

    #region Private Methods

    private static Microsoft.CodeAnalysis.Diagnostic AssertSingle( string id, string source )
    {
        var run = GeneratorHarness.Run( Wrap( source ) );

        var diagnostic = Assert.Single( run.GeneratorDiagnostics );
        Assert.Equal( id, diagnostic.Id );

        return diagnostic;
    }

    internal static string Wrap( string source ) => $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Log0;
        using Microsoft.Extensions.Logging;

        namespace Sample;

        {{source}}
        """;

    #endregion
}
