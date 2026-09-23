using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Log0.Tests;

/// <summary>
/// Touches the process-wide factory, so these never run in parallel with each other.
/// Other test classes are unaffected: <see cref="Log0Logging.Use"/> overrides the global.
/// </summary>
[ Collection( nameof( GlobalFactory ) ) ]
public sealed class LoggerFactoryTests
{
    #region Public Methods

    [ Fact ]
    public void Use_overrides_the_global_factory_and_restores_it_on_dispose()
    {
        var global = new CapturingLoggerFactory();
        var scoped = new CapturingLoggerFactory();
        Log0Logging.Configure( global );

        try
        {
            using ( Log0Logging.Use( scoped ) )
                new Calculator().Add( 1, 1 );

            new Calculator().Add( 2, 2 );

            Assert.Equal( 2, scoped.Entries.Count );
            Assert.Equal( 2, global.Entries.Count );
        }
        finally
        {
            Log0Logging.Configure( null );
        }
    }

    [ Fact ]
    public async Task AddLog0_hands_the_host_logger_factory_to_Log0_when_the_host_starts()
    {
        var factory = new CapturingLoggerFactory();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton< ILoggerFactory >( factory );
        builder.Services.AddLog0();

        using var host = builder.Build();

        try
        {
            await host.StartAsync();
            new Calculator().Add( 1, 2 );
            await host.StopAsync();

            // The host logs its own lifetime messages to the same factory; keep only Log0's.
            Assert.Equal(
                [ "Start Calculator.Add", "Success Calculator.Add" ],
                factory.Entries.Where( e => e.Category == typeof( Calculator ).FullName ).Select( e => e.Message )
            );
        }
        finally
        {
            Log0Logging.Configure( null );
        }
    }

    [ Fact ]
    public void UseLog0_hands_the_factory_over_immediately()
    {
        var factory = new CapturingLoggerFactory();
        var services = new ServiceCollection().AddSingleton< ILoggerFactory >( factory ).BuildServiceProvider();

        try
        {
            services.UseLog0();
            new Calculator().Add( 1, 2 );

            Assert.Equal( 2, factory.Entries.Count );
        }
        finally
        {
            Log0Logging.Configure( null );
        }
    }

    #endregion
}

[ CollectionDefinition( nameof( GlobalFactory ), DisableParallelization = true ) ]
public sealed class GlobalFactory;
