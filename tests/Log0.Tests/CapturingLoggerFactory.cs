using System.Collections.Concurrent;

namespace Log0.Tests;

/// <summary>One log call as a provider saw it.</summary>
public sealed record LogEntry(
    string Category,
    LogLevel Level,
    EventId EventId,
    string Message,
    IReadOnlyDictionary< string, object? > Properties,
    Exception? Exception
);

/// <summary>
/// A logger factory that records everything at or above <see cref="MinimumLevel"/>.
/// </summary>
public sealed class CapturingLoggerFactory( LogLevel minimumLevel = LogLevel.Information ) : ILoggerFactory
{
    #region Fields

    private readonly ConcurrentQueue< LogEntry > _entries = new();

    #endregion

    #region Properties

    public LogLevel MinimumLevel { get; } = minimumLevel;

    public IReadOnlyList< LogEntry > Entries => _entries.ToArray();

    public IReadOnlyList< string > Messages => Entries.Select( e => e.Message ).ToArray();

    #endregion

    #region Public Methods

    public ILogger CreateLogger( string categoryName ) => new CapturingLogger( this, categoryName );

    public void AddProvider( ILoggerProvider provider ) { }

    public void Dispose() { }

    #endregion

    #region Private Methods

    private void Record( LogEntry entry ) => _entries.Enqueue( entry );

    #endregion

    private sealed class CapturingLogger( CapturingLoggerFactory owner, string category ) : ILogger
    {
        public IDisposable? BeginScope< TState >( TState state ) where TState : notnull => null;

        public bool IsEnabled( LogLevel logLevel ) => logLevel != LogLevel.None && logLevel >= owner.MinimumLevel;

        public void Log< TState >( LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func< TState, Exception?, string > formatter )
        {
            if ( !IsEnabled( logLevel ) )
                return;

            var properties = state is IEnumerable< KeyValuePair< string, object? > > pairs
                ? pairs.ToDictionary( p => p.Key, p => p.Value )
                : new Dictionary< string, object? >();

            owner.Record( new LogEntry( category, logLevel, eventId, formatter( state, exception ), properties, exception ) );
        }
    }
}
