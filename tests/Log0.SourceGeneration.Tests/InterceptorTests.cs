using Microsoft.CodeAnalysis;
using Xunit;

namespace Log0.SourceGeneration.Tests;

/// <summary>
/// Every call to a [Log] method is redirected to a generated interceptor, which must compile
/// without a single warning whatever the signature.
/// </summary>
public sealed class InterceptorTests
{
    #region Public Methods

    [ Theory ]
    [ InlineData( NullableContextOptions.Enable ) ]
    [ InlineData( NullableContextOptions.Disable ) ]
    [ InlineData( NullableContextOptions.Annotations ) ]
    public void Compiles_cleanly_whatever_the_nullable_context( NullableContextOptions nullable )
        => AssertIntercepted(
            """
            public class C
            {
                [Log] public string Name( string text, int[] values, List<string> items ) => text;
                [Log] public Task<string> NameAsync( string text ) => Task.FromResult( text );
                [Log] public ValueTask<int> Count( int a ) => new( a );
            }

            public static class Use
            {
                public static async Task Run( C c )
                {
                    c.Name( "a", [ 1 ], [ "b" ] );
                    await c.NameAsync( "a" );
                    await c.Count( 1 );
                }
            }
            """,
            3,
            nullable
        );

    [ Fact ]
    public void Compiles_cleanly_for_awkward_signatures()
        => AssertIntercepted(
            """
            public class C
            {
                [Log] public int Keyword( int @class, string @event ) => @class;
                [Log] public T Constrained<T, TKey>( T value, TKey key ) where T : class?, new() where TKey : struct, IComparable<TKey> => value;
                [Log] internal void Overload( int a ) { }
                [Log] internal void Overload( string a ) { }
                [Log] public int Refs( in int a, ref readonly int b, ref int c, out int d, scoped Span<int> e ) { d = 1; return a + b + c; }
                [Log] public virtual Task Virtual() => Task.CompletedTask;
                [Log] public ValueTask<int?> Nullable( int? a, string? b ) => new( a );
                [Log] public ValueTask Done() => default;
                [Log] public static int Static( int x ) => x;
                [Log] public int Params( params int[] values ) => values.Length;
                [Log] public string Method( string Method ) => Method;
                [Log] public unsafe void Pointer( int* p ) { }
                [Log] public int Receiver( int __log0_this ) => __log0_this;
            }

            public static class Extensions
            {
                [Log] public static string Shout( this string text ) => text.ToUpperInvariant();
            }

            public class Generic<T> where T : notnull
            {
                [Log] public Task<T> Get( T value ) => Task.FromResult( value );
                [Log] public U Map<U>( T value, Func<T, U> map ) => map( value );

                public class Nested<V>
                {
                    [Log] public (T, V) Pair( T t, V v ) => (t, v);
                }
            }

            public static class Use
            {
                public static async Task Run( C c, Generic<string> g, Generic<int>.Nested<bool> n )
                {
                    c.Keyword( 1, "e" );
                    c.Constrained<object, int>( new object(), 1 );
                    c.Overload( 1 );
                    c.Overload( "1" );
                    int x = 1;
                    c.Refs( in x, in x, ref x, out var y, stackalloc int[ 1 ] );
                    await c.Virtual();
                    await c.Nullable( null, null );
                    await c.Done();
                    C.Static( 1 );
                    c.Params( 1, 2, 3 );
                    c.Params();
                    c.Method( "m" );
                    c.Receiver( 1 );
                    "a".Shout();
                    Extensions.Shout( "a" );
                    await g.Get( "v" );
                    g.Map( "v", s => s.Length );
                    n.Pair( 1, true );
                }

                public static unsafe void Unsafe( C c, int x ) => c.Pointer( &x );

                public static T Open<T>( Generic<T> g, T value ) where T : notnull => g.Map( value, v => v );
            }
            """,
            20
        );

    [ Fact ]
    public void Calls_through_an_interface_are_intercepted_when_the_interface_method_has_Log()
        => AssertIntercepted(
            """
            public interface IOrders
            {
                [Log] Task<int> Get( int id, CancellationToken ct );
            }

            public sealed class Orders : IOrders
            {
                public Task<int> Get( int id, CancellationToken ct ) => Task.FromResult( id );
            }

            public static class Use
            {
                public static Task<int> Run( IOrders orders ) => orders.Get( 1, default );
            }
            """,
            1
        );

    [ Fact ]
    public void Calls_inside_the_type_are_intercepted_but_base_calls_are_not()
    {
        var run = AssertIntercepted(
            """
            public class Base
            {
                [Log] public virtual int Get() => 1;
            }

            public class Derived : Base
            {
                public override int Get() => base.Get() + 1;
                public int Twice() => Get() + this.Get();
            }
            """,
            2
        );

        Assert.DoesNotContain( "base.", string.Join( "\n", run.Generated ) );
    }

    [ Fact ]
    public void Logs_under_the_declaring_types_category_with_masked_parameters()
    {
        var run = AssertIntercepted(
            """
            [NoLog] public sealed record Card( string Number );

            public class Generic<T>
            {
                [Log( LogLevel.Debug )] public void Pay( string user, [NoLog] string pin, Card card, CancellationToken ct ) { }
            }

            public static class Use
            {
                public static void Run( Generic<int> g ) => g.Pay( "u", "1234", new Card( "4111" ), default );
            }
            """,
            1
        );

        var generated = Assert.Single( run.Generated );
        Assert.Contains( "typeof( global::Sample.Generic<> )", generated );
        Assert.Contains( "new( \"Generic.Pay\", (global::Microsoft.Extensions.Logging.LogLevel)1, \"user\", \"pin\", \"card\" )", generated );
        Assert.Contains( "{ user, global::Log0.Log0Method.Redacted, global::Log0.Log0Method.Redacted }", generated );
    }

    [ Fact ]
    public void Every_call_to_the_same_method_shares_one_interceptor()
    {
        var run = AssertIntercepted(
            """
            public class C
            {
                [Log] public int Get( int x ) => x;
                public int Use() => Get( 1 ) + Get( 2 ) + Get( 3 );
            }
            """,
            3
        );

        var generated = Assert.Single( run.Generated );
        Assert.Single( generated.Split( '\n' ), l => l.Contains( "internal static int Get(" ) );
    }

    [ Fact ]
    public void Parameters_named_like_generated_identifiers_do_not_collide()
        => AssertIntercepted(
            """
            public class C
            {
                [Log] public int Sync( int __logger, int __log, int __result, int __exception, int __log0Logger, int __log0_Sync_0 ) => __logger;
                [Log] public Task<int> Async( int __task, int __log0_task, int __Log0Await ) => Task.FromResult( __task );
                [Log] public ValueTask<__log0> Generic<__log0>( __log0 __log0_result ) => new( __log0_result );
                public void Use() { Sync( 1, 2, 3, 4, 5, 6 ); Async( 1, 2, 3 ); Generic( 1 ); }
            }
            """,
            3
        );

    [ Fact ]
    public void Global_namespace_types_are_supported()
    {
        var run = GeneratorHarness.Run(
            """
            using Log0;
            public class C
            {
                [Log] public void Go() { }
                public void Use() => Go();
            }
            """
        );

        Assert.Empty( run.CompilerDiagnostics );
        Assert.Contains( "namespace Log0.Generated\n", Assert.Single( run.Generated ) );
    }

    #endregion

    #region Private Methods

    /// <summary>Asserts a clean compile and that exactly <paramref name="calls"/> call sites were intercepted.</summary>
    private static GeneratorRun AssertIntercepted( string source, int calls, NullableContextOptions nullable = NullableContextOptions.Enable )
    {
        var run = GeneratorHarness.Run( DiagnosticTests.Wrap( source ), nullable );

        Assert.Empty( run.GeneratorDiagnostics );
        Assert.True( run.CompilerDiagnostics.Count == 0, run.Describe() );

        var sites = run.Generated.Sum( g => g.Split( '\n' ).Count( l => l.Contains( "InterceptsLocationAttribute(1," ) ) );
        Assert.True( sites == calls, $"Expected {calls} intercepted calls, found {sites}\n{run.Describe()}" );

        return run;
    }

    #endregion
}
