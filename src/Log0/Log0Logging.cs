using Microsoft.Extensions.Logging;

namespace Log0;

/// <summary>
/// Where generated code gets its loggers from, when the class has no <see cref="ILogger"/> of
/// its own.
/// </summary>
/// <remarks>
/// In an app with a host, call <c>services.AddLog0()</c> and this is set for you when the host
/// starts. Until a factory is set, logging does nothing.
/// </remarks>
public static class Log0Logging
{
    #region Fields

    private static readonly AsyncLocal< Override? > _override = new();
    private static ILoggerFactory? _global;

    #endregion

    #region Properties

    /// <summary>
    /// The factory in effect for the current async flow: the innermost <see cref="Use"/> if any,
    /// otherwise the one given to <see cref="Configure"/>.
    /// </summary>
    public static ILoggerFactory? Current
    {
        get
        {
            var scoped = _override.Value;

            return scoped is null ? Volatile.Read( ref _global ) : scoped.Factory;
        }
    }

    #endregion

    #region Public Methods

    /// <summary>Sets the process-wide factory. Pass <see langword="null"/> to stop logging.</summary>
    public static void Configure( ILoggerFactory? loggerFactory ) => Volatile.Write( ref _global, loggerFactory );

    /// <summary>
    /// Uses <paramref name="loggerFactory"/> for the current async flow only, until the result is
    /// disposed. Meant for tests, which can run in parallel without seeing each other's logs.
    /// </summary>
    /// <param name="loggerFactory">The factory to use, or <see langword="null"/> to log nothing.</param>
    public static IDisposable Use( ILoggerFactory? loggerFactory )
    {
        var previous = _override.Value;
        _override.Value = new Override( loggerFactory );

        return new Restore( previous );
    }

    #endregion

    private sealed class Override( ILoggerFactory? factory )
    {
        public ILoggerFactory? Factory { get; } = factory;
    }

    private sealed class Restore( Override? previous ) : IDisposable
    {
        public void Dispose() => _override.Value = previous;
    }
}
