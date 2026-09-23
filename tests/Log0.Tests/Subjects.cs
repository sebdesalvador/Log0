using System.Threading.Tasks.Sources;

namespace Log0.Tests;

public sealed record Credentials( string User, string Password );

[ NoLog ]
public sealed record ApiKey( string Value );

public readonly record struct Money( decimal Amount, string Currency );

public partial class Calculator
{
    [ Log ]
    public partial int Add( int a, int b );

    private int AddCore( int a, int b ) => a + b;

    [ Log ]
    public partial void Fail( string reason );

    private void FailCore( string reason ) => throw new InvalidOperationException( reason );

    [ Log ]
    public partial void Cancel();

    private void CancelCore() => throw new OperationCanceledException();

    [ Log( LogLevel.Debug ) ]
    public partial int Quiet( int x );

    private int QuietCore( int x ) => x;

    [ Log( LogLevel.Debug ) ]
    public partial void QuietFail();

    private void QuietFailCore() => throw new InvalidOperationException( "quiet" );

    [ Log ]
    public partial Money Convert( Money money, string currency );

    private Money ConvertCore( Money money, string currency ) => money with { Currency = currency };

    [ Log ]
    public partial int? Find( int key );

    private int? FindCore( int key ) => key > 0 ? key : null;

    [ Log ]
    public static partial int Twice( int x );

    private static int TwiceCore( int x ) => x * 2;

    [ Log ]
    public partial T Echo< T >( T value ) where T : notnull;

    private T EchoCore< T >( T value ) where T : notnull => value;

    [ Log ]
    public partial bool TryParse( string text, out int value );

    private bool TryParseCore( string text, out int value ) => int.TryParse( text, out value );

    [ Log ]
    public partial void Increment( ref int counter );

    private void IncrementCore( ref int counter ) => counter++;

    [ Log ]
    public partial int Sum( params int[ ] values );

    private int SumCore( params int[ ] values ) => values.Sum();

    [ Log ]
    public partial int Length( ReadOnlySpan< char > text, string label );

    private int LengthCore( ReadOnlySpan< char > text, string label ) => text.Length;

    [ Log ]
    public partial string Describe( string? text, IEnumerable< int > numbers, DateTime when );

    private string DescribeCore( string? text, IEnumerable< int > numbers, DateTime when ) => text ?? "";
}

public partial class Vault
{
    [ Log ]
    public partial bool Login( string user, [ NoLog ] string password );

    private bool LoginCore( string user, string password ) => password == "secret";

    [ Log ]
    public partial bool Check( Credentials credentials, ApiKey key );

    private bool CheckCore( Credentials credentials, ApiKey key ) => true;

    [ Log, NoLog ]
    public partial void Store( string secret, int slot );

    private void StoreCore( string secret, int slot ) { }
}

public partial class Downloader
{
    [ Log ]
    public partial Task< string > Get( string url, CancellationToken cancellationToken );

    private async Task< string > GetCore( string url, CancellationToken cancellationToken )
    {
        await Task.Delay( 10, cancellationToken );
        return url.ToUpperInvariant();
    }

    [ Log ]
    public partial Task Wait( Task gate );

    private Task WaitCore( Task gate ) => gate;

    [ Log ]
    public partial Task< int > Pending( Task< int > gate );

    private Task< int > PendingCore( Task< int > gate ) => gate;

    [ Log ]
    public partial Task Explode();

    private async Task ExplodeCore()
    {
        await Task.Yield();
        throw new InvalidOperationException( "async boom" );
    }

    [ Log ]
    public partial Task ThrowEarly();

    // Not async: the exception is thrown synchronously, before any task exists.
    private Task ThrowEarlyCore() => throw new ArgumentException( "sync boom" );

    [ Log ]
    public partial ValueTask< int > Measure( ValueTask< int > source );

    private ValueTask< int > MeasureCore( ValueTask< int > source ) => source;

    [ Log ]
    public partial ValueTask Flush( ValueTask source );

    private ValueTask FlushCore( ValueTask source ) => source;
}

public partial class InjectedLoggerService( ILogger< InjectedLoggerService > logger )
{
    private readonly ILogger _logger = logger;

    [ Log ]
    public partial void Run();

    private void RunCore() { }
}

public partial class Repository< TEntity > where TEntity : class
{
    [ Log ]
    public partial TEntity? Load( int id );

    private TEntity? LoadCore( int id ) => null;
}

public static partial class Outer
{
    public partial class Inner
    {
        [ Log ]
        public partial string Hello( string name );

        private string HelloCore( string name ) => $"Hello {name}";
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
