using System.Threading.Tasks.Sources;

namespace Log0.Tests;

public sealed record Credentials( string User, string Password );

[ NoLog ]
public sealed record ApiKey( string Value );

public readonly record struct Money( decimal Amount, string Currency );

public class Calculator
{
    [ Log ]
    public int Add( int a, int b ) => a + b;

    [ Log ]
    public void Fail( string reason ) => throw new InvalidOperationException( reason );

    [ Log ]
    public void Cancel() => throw new OperationCanceledException();

    [ Log( LogLevel.Debug ) ]
    public int Quiet( int x ) => x;

    [ Log( LogLevel.Debug ) ]
    public void QuietFail() => throw new InvalidOperationException( "quiet" );

    [ Log ]
    public Money Convert( Money money, string currency ) => money with { Currency = currency };

    [ Log ]
    public int? Find( int key ) => key > 0 ? key : null;

    [ Log ]
    public static int Twice( int x ) => x * 2;

    [ Log ]
    public T Echo< T >( T value ) where T : notnull => value;

    [ Log ]
    public bool TryParse( string text, out int value ) => int.TryParse( text, out value );

    [ Log ]
    public void Increment( ref int counter ) => counter++;

    [ Log ]
    public int Sum( params int[ ] values ) => values.Sum();

    [ Log ]
    public int Length( ReadOnlySpan< char > text, string label ) => text.Length;

    [ Log ]
    public string Describe( string? text, IEnumerable< int > numbers, DateTime when ) => text ?? "";
}

public class Vault
{
    [ Log ]
    public bool Login( string user, [ NoLog ] string password ) => password == "secret";

    [ Log ]
    public bool Check( Credentials credentials, ApiKey key ) => true;

    [ Log, NoLog ]
    public void Store( string secret, int slot ) { }
}

public class Downloader
{
    [ Log ]
    public async Task< string > Get( string url, CancellationToken cancellationToken )
    {
        await Task.Delay( 10, cancellationToken );
        return url.ToUpperInvariant();
    }

    [ Log ]
    public Task Wait( Task gate ) => gate;

    [ Log ]
    public Task< int > Pending( Task< int > gate ) => gate;

    [ Log ]
    public async Task Explode()
    {
        await Task.Yield();
        throw new InvalidOperationException( "async boom" );
    }

    // Not async: the exception is thrown synchronously, before any task exists.
    [ Log ]
    public Task ThrowEarly() => throw new ArgumentException( "sync boom" );

    [ Log ]
    public ValueTask< int > Measure( ValueTask< int > source ) => source;

    [ Log ]
    public ValueTask Flush( ValueTask source ) => source;
}

public class Repository< TEntity > where TEntity : class
{
    [ Log ]
    public TEntity? Load( int id ) => null;
}

public static class Outer
{
    public class Inner
    {
        [ Log ]
        public string Hello( string name ) => $"Hello {name}";
    }
}

/// <summary>
/// A ValueTask source that fails if its result is read twice, the way a pooled source would
/// misbehave. Proves the generated wrapper consumes a ValueTask exactly once.
/// </summary>
public sealed class SingleUseSource< T >( T result, bool completeSynchronously ) : IValueTaskSource< T >, IValueTaskSource
{
    private ManualResetValueTaskSourceCore< T > _core = new() { RunContinuationsAsynchronously = true };
    private int _reads;

    public ValueTask< T > Typed() { Start(); return new ValueTask< T >( this, _core.Version ); }

    public ValueTask Untyped() { Start(); return new ValueTask( this, _core.Version ); }

    public int Reads => _reads;

    private void Start()
    {
        if ( completeSynchronously )
            _core.SetResult( result );
        else
            _ = Task.Run( async () => { await Task.Delay( 10 ); _core.SetResult( result ); } );
    }

    public T GetResult( short token )
    {
        if ( Interlocked.Increment( ref _reads ) > 1 )
            throw new InvalidOperationException( "ValueTask consumed twice" );

        return _core.GetResult( token );
    }

    void IValueTaskSource.GetResult( short token ) => GetResult( token );

    public ValueTaskSourceStatus GetStatus( short token ) => _core.GetStatus( token );

    public void OnCompleted( Action< object? > continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags )
        => _core.OnCompleted( continuation, state, token, flags );
}
