using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Log0;

/// <summary>
/// A class's logger from <see cref="Log0Logging.Current"/>, created on first use and recreated
/// when the factory changes. Generated code holds one per class; not meant to be used directly.
/// </summary>
[ EditorBrowsable( EditorBrowsableState.Never ) ]
public sealed class Log0Logger( Type category )
{
    #region Fields

    private Cached? _cached;

    #endregion

    #region Properties

    /// <summary>The logger for the category, or a no-op logger when no factory is set.</summary>
    public ILogger Logger
    {
        get
        {
            var factory = Log0Logging.Current;

            if ( factory is null )
                return NullLogger.Instance;

            var cached = _cached;

            if ( cached is not null && ReferenceEquals( cached.Factory, factory ) )
                return cached.Logger;

            // Same category name as ILogger<T>, so appsettings filters by type name apply.
            var logger = factory.CreateLogger( category );
            _cached = new Cached( factory, logger );

            return logger;
        }
    }

    #endregion

    private sealed class Cached( ILoggerFactory factory, ILogger logger )
    {
        public ILoggerFactory Factory { get; } = factory;

        public ILogger Logger { get; } = logger;
    }
}
