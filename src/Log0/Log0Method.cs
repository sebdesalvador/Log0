using System.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Log0;

/// <summary>
/// Everything known at compile time about one logged method, and the log calls it makes.
/// Generated code holds one per method; not meant to be used directly.
/// </summary>
[ EditorBrowsable( EditorBrowsableState.Never ) ]
public sealed class Log0Method
{
    #region Constants

    /// <summary>What a <see cref="NoLogAttribute">[NoLog]</see> value is logged as.</summary>
    public const string Redacted = "***";

    private const string METHOD_KEY = "Method";

    #endregion

    #region Fields

    private static readonly EventId _startEvent = new( 1, "Start" );
    private static readonly EventId _successEvent = new( 2, "Success" );
    private static readonly EventId _canceledEvent = new( 3, "Canceled" );
    private static readonly EventId _errorEvent = new( 4, "Error" );

    private readonly string[ ] _parameterKeys;
    private readonly string _startWithParametersFormat;

    #endregion

    #region Properties

    /// <summary>The type and method, as it appears in messages: <c>OrderService.GetOrder</c>.</summary>
    public string Name { get; }

    /// <summary>The level of <c>Start</c> and <c>Success</c>.</summary>
    public LogLevel Level { get; }

    #endregion

    #region Constructors

    /// <param name="name">The type and method, as it appears in messages.</param>
    /// <param name="level">The level of <c>Start</c> and <c>Success</c>.</param>
    /// <param name="parameterNames">
    /// The parameters whose values are logged, in the order <see cref="Start"/> receives them.
    /// </param>
    public Log0Method( string name, LogLevel level, params string[ ] parameterNames )
    {
        Name = name;
        Level = level;
        // A parameter called "Method" would otherwise shadow the method name property.
        _parameterKeys = parameterNames.Select( p => p == METHOD_KEY ? p + "_" : p ).ToArray();
        _startWithParametersFormat = "Start {" + METHOD_KEY + "}("
                                   + string.Join( ", ", _parameterKeys.Select( k => k + ": {" + k + "}" ) )
                                   + ")";
    }

    #endregion

    #region Public Methods

    /// <summary>Whether <c>Start</c> and <c>Success</c> would be written.</summary>
    public bool IsEnabled( ILogger logger ) => logger.IsEnabled( Level );

    /// <summary>Whether <c>Start</c> should carry parameter values: only at Debug or lower.</summary>
    public bool IncludesParameters( ILogger logger ) => _parameterKeys.Length > 0 && logger.IsEnabled( LogLevel.Debug );

    /// <summary>Writes <c>Start</c>, with <paramref name="values"/> when given.</summary>
    public void Start( ILogger logger, object?[ ]? values )
    {
        if ( !logger.IsEnabled( Level ) )
            return;

        var state = values is null
            ? new Log0State( "Start {" + METHOD_KEY + "}", "Start", Name, null, null )
            : new Log0State( _startWithParametersFormat, "Start", Name, _parameterKeys, values );

        logger.Log( Level, _startEvent, state, null, Log0State.Format );
    }

    /// <summary>Writes <c>Success</c>.</summary>
    public void Success( ILogger logger )
    {
        if ( logger.IsEnabled( Level ) )
            logger.Log( Level, _successEvent, Outcome( "Success" ), null, Log0State.Format );
    }

    /// <summary>
    /// Writes <c>Canceled</c> (warning) for an <see cref="OperationCanceledException"/>, otherwise
    /// <c>Error</c> (error). Always returns <see langword="false"/>, so it can run as an exception
    /// filter: the log is written before the stack unwinds, and the exception is never caught.
    /// </summary>
    public bool Failed( ILogger logger, Exception exception )
    {
        if ( exception is OperationCanceledException )
        {
            if ( logger.IsEnabled( LogLevel.Warning ) )
                logger.Log( LogLevel.Warning, _canceledEvent, Outcome( "Canceled" ), null, Log0State.Format );
        }
        else if ( logger.IsEnabled( LogLevel.Error ) )
            logger.Log( LogLevel.Error, _errorEvent, Outcome( "Error" ), exception, Log0State.Format );

        return false;
    }

    #endregion

    #region Private Methods

    private Log0State Outcome( string outcome ) => new( outcome + " {" + METHOD_KEY + "}", outcome, Name, null, null );

    #endregion
}
