using Log0;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Where AddLogging and friends live, so AddLog0 shows up without an extra using.
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Connects Log0 to an app's <see cref="ILoggerFactory"/>.</summary>
public static class Log0ServiceCollectionExtensions
{
    #region Public Methods

    /// <summary>
    /// Hands the app's <see cref="ILoggerFactory"/> to Log0 when the host starts. Calls made
    /// before the host starts are not logged; call <see cref="UseLog0"/> after building the
    /// host if they should be.
    /// </summary>
    public static IServiceCollection AddLog0( this IServiceCollection services )
    {
        services.AddHostedService< Log0HostedService >();

        return services;
    }

    /// <summary>Hands the provider's <see cref="ILoggerFactory"/> to Log0 immediately.</summary>
    public static IServiceProvider UseLog0( this IServiceProvider services )
    {
        Log0Logging.Configure( services.GetRequiredService< ILoggerFactory >() );

        return services;
    }

    #endregion

    private sealed class Log0HostedService( ILoggerFactory loggerFactory ) : IHostedService
    {
        public Task StartAsync( CancellationToken cancellationToken )
        {
            Log0Logging.Configure( loggerFactory );

            return Task.CompletedTask;
        }

        public Task StopAsync( CancellationToken cancellationToken ) => Task.CompletedTask;
    }
}
