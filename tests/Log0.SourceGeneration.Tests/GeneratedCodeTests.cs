using Microsoft.CodeAnalysis;
using Xunit;

namespace Log0.SourceGeneration.Tests;

/// <summary>
/// Shapes the runtime tests can't easily cover: the generated code must compile without a
/// single warning, whatever the user's code looks like.
/// </summary>
public sealed class GeneratedCodeTests
{
    #region Public Methods

    [ Theory ]
    [ InlineData( NullableContextOptions.Enable ) ]
    [ InlineData( NullableContextOptions.Disable ) ]
    [ InlineData( NullableContextOptions.Annotations ) ]
    public void Compiles_cleanly_whatever_the_nullable_context( NullableContextOptions nullable )
        => AssertClean(
            """
            public partial class C
            {
                [Log] public partial string Name( string text, int[] values, List<string> items );
                private string NameCore( string text, int[] values, List<string> items ) => text;

                [Log] public partial Task<string> NameAsync( string text );
                private Task<string> NameAsyncCore( string text ) => Task.FromResult( text );
            }
            """,
            nullable
        );

    [ Fact ]
    public void Compiles_cleanly_for_awkward_signatures()
        => AssertClean(
            """
            public partial class C
            {
                [Log] public partial int Keyword( int @class, string @event );
                private int KeywordCore( int @class, string @event ) => @class;

                [Log] public partial T Constrained<T, TKey>( T value, TKey key ) where T : class?, new() where TKey : struct, IComparable<TKey>;
                private T ConstrainedCore<T, TKey>( T value, TKey key ) where T : class?, new() where TKey : struct, IComparable<TKey> => value;

                [Log] internal partial void Overload( int a );
                private void OverloadCore( int a ) { }

                [Log] internal partial void Overload( string a );
                private void OverloadCore( string a ) { }

                [Log] public partial int Refs( in int a, ref readonly int b, out int c, scoped Span<int> d );
                private int RefsCore( in int a, ref readonly int b, out int c, scoped Span<int> d ) { c = 1; return a + b; }

                [Log] public partial ValueTask<int?> Nullable( int? a, string? b );
                private ValueTask<int?> NullableCore( int? a, string? b ) => new( a );

                [Log] public virtual partial Task Virtual();
                private Task VirtualCore() => Task.CompletedTask;

                [Log] public partial string Method( string Method );
                private string MethodCore( string Method ) => Method;

                [Log] public unsafe partial void Pointer( int* p );
                private unsafe void PointerCore( int* p ) { }
            }

            public static partial class Extensions
            {
                [Log] public static partial string Shout( this string text );
                private static string ShoutCore( this string text ) => text.ToUpperInvariant();
            }

            public readonly partial record struct Point( int X, int Y )
            {
                [Log] public partial int Sum();
                private int SumCore() => X + Y;
            }

            public partial record Person( string Name )
            {
                [Log] public partial string Greet( Person other );
                private string GreetCore( Person other ) => Name + other.Name;
            }

            public ref partial struct Cursor
            {
                [Log] public partial int Next();
                private int NextCore() => 1;
            }

            public partial class Generic<T> where T : notnull
            {
                [Log] public partial Task<T> Get( T value );
                private Task<T> GetCore( T value ) => Task.FromResult( value );
            }
            """
        );

    [ Fact ]
    public void Parameters_named_like_generated_identifiers_do_not_collide()
        => AssertClean(
            """
            public partial class C
            {
                [Log] public partial int Sync( int __logger, int __log, int __result, int __exception, int __log0Logger, int __log0_Sync_0 );
                private int SyncCore( int __logger, int __log, int __result, int __exception, int __log0Logger, int __log0_Sync_0 ) => __logger;

                [Log] public partial Task<int> Async( int __task, int __log0_task, int __Log0Await );
                private Task<int> AsyncCore( int __task, int __log0_task, int __Log0Await ) => Task.FromResult( __task );

                [Log] public partial ValueTask<__log0> Generic<__log0>( __log0 __log0_result );
                private ValueTask<__log0> GenericCore<__log0>( __log0 __log0_result ) => new( __log0_result );
            }
            """
        );

    [ Fact ]
    public void Core_overloads_are_matched_by_parameter_types()
    {
        // The int overload is fine; the string one is async void and must be reported,
        // whichever overload the compiler happens to list first.
        var run = GeneratorHarness.Run( DiagnosticTests.Wrap(
            """
            public partial class C
            {
                [Log] public partial void Go( string a );
                private void GoCore( int a ) { }
                private async void GoCore( string a ) => await Task.Yield();
            }
            """
        ) );

        Assert.Equal( "LOG004", Assert.Single( run.GeneratorDiagnostics ).Id );
    }

    [ Fact ]
    public void Uses_an_existing_logger_field_property_or_base_member()
    {
        var run = AssertClean(
            """
            public partial class WithField
            {
                private readonly ILogger<WithField> _logger;
                public WithField( ILogger<WithField> logger ) => _logger = logger;
                [Log] public partial void Run();
                private void RunCore() { }
            }

            public abstract class Base
            {
                protected ILogger Logger { get; } = null!;
            }

            public partial class Derived : Base
            {
                [Log] public partial void Run();
                private void RunCore() { }

                [Log] public static partial void Static();
                private static void StaticCore() { }
            }
            """
        );

        var all = string.Join( "\n", run.Generated );
        Assert.Contains( "this._logger", all );
        Assert.Contains( "this.Logger", all );
        // Static method, instance-only logger: falls back to the shared factory.
        Assert.Contains( "__log0Logger.Logger", all );
    }

    [ Fact ]
    public void Masks_NoLog_parameters_and_types_without_reading_them()
    {
        var run = AssertClean(
            """
            [NoLog] public interface ISecret { }
            public sealed class Token : ISecret { }
            [NoLog] public struct Pin { }

            public partial class C
            {
                [Log] public partial void Go( string user, [NoLog] string password, Token token, Pin? pin, Pin[] pins, CancellationToken ct );
                private void GoCore( string user, string password, Token token, Pin? pin, Pin[] pins, CancellationToken ct ) { }
            }
            """
        );

        var generated = Assert.Single( run.Generated );
        Assert.Contains(
            "new( \"C.Go\", (global::Microsoft.Extensions.Logging.LogLevel)2, \"user\", \"password\", \"token\", \"pin\", \"pins\" )",
            generated
        );
        Assert.Contains(
            "{ user, global::Log0.Log0Method.Redacted, global::Log0.Log0Method.Redacted, global::Log0.Log0Method.Redacted, global::Log0.Log0Method.Redacted }",
            generated
        );
    }

    [ Fact ]
    public void Level_comes_from_the_attribute()
    {
        var run = AssertClean(
            """
            public partial class C
            {
                [Log( LogLevel.Trace )] public partial void Go();
                private void GoCore() { }
            }
            """
        );

        Assert.Contains( "(global::Microsoft.Extensions.Logging.LogLevel)0", Assert.Single( run.Generated ) );
    }

    [ Fact ]
    public void Global_namespace_types_are_supported()
    {
        var run = GeneratorHarness.Run(
            """
            using Log0;
            public partial class C
            {
                [Log] public partial void Go();
                private void GoCore() { }
            }
            """
        );

        Assert.Empty( run.CompilerDiagnostics );
        Assert.DoesNotContain( "namespace", Assert.Single( run.Generated ) );
    }

    #endregion

    #region Private Methods

    private static GeneratorRun AssertClean( string source, NullableContextOptions nullable = NullableContextOptions.Enable )
    {
        var run = GeneratorHarness.Run( DiagnosticTests.Wrap( source ), nullable );

        Assert.Empty( run.GeneratorDiagnostics );
        Assert.True( run.CompilerDiagnostics.Count == 0, run.Describe() );

        return run;
    }

    #endregion
}
