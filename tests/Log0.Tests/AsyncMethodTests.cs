namespace Log0.Tests;

public sealed class AsyncMethodTests
{
    #region Public Methods

    [ Fact ]
    public async Task Task_of_T_logs_success_after_completion()
    {
        var factory = new CapturingLoggerFactory( LogLevel.Debug );
        using var _ = Log0Logging.Use( factory );

        var result = await new Downloader().Get( "a", CancellationToken.None );

        Assert.Equal( "A", result );
        Assert.Equal( [ "Start Downloader.Get(url: a)", "Success Downloader.Get" ], factory.Messages );
    }

    [ Fact ]
    public async Task Success_is_not_logged_until_the_task_completes()
    {
        var factory = new CapturingLoggerFactory();
        using var _ = Log0Logging.Use( factory );
        var gate = new TaskCompletionSource< int >( TaskCreationOptions.RunContinuationsAsynchronously );

        var pending = new Downloader().Pending( gate.Task );

        Assert.Equal( [ "Start Downloader.Pending" ], factory.Messages );
        gate.SetResult( 5 );
        Assert.Equal( 5, await pending );
        Assert.Equal( [ "Start Downloader.Pending", "Success Downloader.Pending" ], factory.Messages );
    }

    [ Fact ]
    public async Task Plain_task_logs_success_after_completion()
    {
        var factory = new CapturingLoggerFactory();
        using var _ = Log0Logging.Use( factory );
        var gate = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );

        var pending = new Downloader().Wait( gate.Task );
        Assert.Single( factory.Entries );

        gate.SetResult();
        await pending;
        Assert.Equal( "Success Downloader.Wait", factory.Messages[ 1 ] );
    }

    [ Fact ]
    public async Task Faulted_task_logs_error_and_rethrows()
    {
        var factory = new CapturingLoggerFactory();
        using var _ = Log0Logging.Use( factory );

        var thrown = await Assert.ThrowsAsync< InvalidOperationException >( () => new Downloader().Explode() );

        Assert.Equal( [ "Start Downloader.Explode", "Error Downloader.Explode" ], factory.Messages );
        Assert.Same( thrown, factory.Entries[ 1 ].Exception );
    }

    [ Fact ]
    public void Synchronous_throw_stays_synchronous()
    {
        var factory = new CapturingLoggerFactory();
        using var _ = Log0Logging.Use( factory );

        // Thrown by the call itself, not stored in a returned task.
        Assert.Throws< ArgumentException >( () => { var task = new Downloader().ThrowEarly(); } );
        Assert.Equal( "Error Downloader.ThrowEarly", factory.Messages[ 1 ] );
    }

    [ Fact ]
    public async Task Canceled_task_logs_canceled_as_a_warning()
    {
        var factory = new CapturingLoggerFactory();
        using var _ = Log0Logging.Use( factory );

        await Assert.ThrowsAnyAsync< OperationCanceledException >(
            () => new Downloader().Get( "a", new CancellationToken( canceled: true ) )
        );

        Assert.Equal( [ "Start Downloader.Get", "Canceled Downloader.Get" ], factory.Messages );
        Assert.Equal( LogLevel.Warning, factory.Entries[ 1 ].Level );
    }

    [ Theory ]
    [ InlineData( true ) ]
    [ InlineData( false ) ]
    public async Task ValueTask_of_T_is_consumed_exactly_once( bool completeSynchronously )
    {
        var factory = new CapturingLoggerFactory();
        using var _ = Log0Logging.Use( factory );
        var source = new SingleUseSource< int >( 42, completeSynchronously );

        var result = await new Downloader().Measure( source.Typed() );

        Assert.Equal( 42, result );
        Assert.Equal( 1, source.Reads );
        Assert.Equal( [ "Start Downloader.Measure", "Success Downloader.Measure" ], factory.Messages );
    }

    [ Theory ]
    [ InlineData( true ) ]
    [ InlineData( false ) ]
    public async Task ValueTask_is_consumed_exactly_once( bool completeSynchronously )
    {
        var factory = new CapturingLoggerFactory();
        using var _ = Log0Logging.Use( factory );
        var source = new SingleUseSource< int >( 1, completeSynchronously );

        await new Downloader().Flush( source.Untyped() );

        Assert.Equal( 1, source.Reads );
        Assert.Equal( "Success Downloader.Flush", factory.Messages[ 1 ] );
    }

    [ Fact ]
    public async Task Faulted_ValueTask_logs_error()
    {
        var factory = new CapturingLoggerFactory();
        using var _ = Log0Logging.Use( factory );

        await Assert.ThrowsAsync< TimeoutException >(
            async () => await new Downloader().Measure( ValueTask.FromException< int >( new TimeoutException() ) )
        );

        Assert.Equal( "Error Downloader.Measure", factory.Messages[ 1 ] );
    }

    #endregion
}
