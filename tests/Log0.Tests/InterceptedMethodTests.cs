namespace Log0.Tests;

/// <summary>Plain [Log] methods: every call is intercepted and logged, with no extra code.</summary>
public sealed class InterceptedMethodTests
{
    #region Public Methods

    [ Fact ]
    public void Success_logs_start_then_success_under_the_declaring_types_category()
    {
        var factory = new CapturingLoggerFactory();
        using var _ = Log0Logging.Use( factory );

        var count = new Inventory().Count( "apple" );

        Assert.Equal( 3, count );
        Assert.Equal( [ "Start Inventory.Count", "Success Inventory.Count" ], factory.Messages );
        Assert.All( factory.Entries, e => Assert.Equal( "Log0.Tests.Inventory", e.Category ) );
        Assert.Equal( [ "Start", "Success" ], factory.Entries.Select( e => e.EventId.Name ) );
    }

    [ Fact ]
    public void Exception_logs_error_and_propagates_from_the_original_method()
    {
        var factory = new CapturingLoggerFactory();
        using var _ = Log0Logging.Use( factory );

        var thrown = Assert.Throws< KeyNotFoundException >( () => new Inventory().Remove( "pear" ) );

        Assert.Equal( [ "Start Inventory.Remove", "Error Inventory.Remove" ], factory.Messages );
        Assert.Same( thrown, factory.Entries[ 1 ].Exception );
        Assert.Contains( nameof( Inventory.Remove ), thrown.StackTrace );
    }

    [ Fact ]
    public async Task Async_methods_log_success_when_the_task_completes()
    {
        var factory = new CapturingLoggerFactory();
        using var _ = Log0Logging.Use( factory );

        var count = await new Inventory().CountAsync( "apple", CancellationToken.None );

        Assert.Equal( 3, count );
        // The nested Count call inside CountAsync is intercepted too.
        Assert.Equal(
            [ "Start Inventory.CountAsync", "Start Inventory.Count", "Success Inventory.Count", "Success Inventory.CountAsync" ],
            factory.Messages
        );
    }

    [ Fact ]
    public async Task Canceled_and_failed_tasks_are_logged_when_they_settle()
    {
        var factory = new CapturingLoggerFactory();
        using var _ = Log0Logging.Use( factory );
        var inventory = new Inventory();

        await Assert.ThrowsAnyAsync< OperationCanceledException >( () => inventory.CountAsync( "apple", new CancellationToken( true ) ) );
        await Assert.ThrowsAsync< KeyNotFoundException >( async () => await inventory.RemoveAsync( "pear" ) );

        Assert.Equal(
            [
                "Start Inventory.CountAsync", "Canceled Inventory.CountAsync",
                "Start Inventory.RemoveAsync", "Start Inventory.Remove", "Error Inventory.Remove", "Error Inventory.RemoveAsync"
            ],
            factory.Messages
        );
    }

    [ Fact ]
    public void Debug_methods_log_parameters_and_mask_NoLog_values()
    {
        var factory = new CapturingLoggerFactory( LogLevel.Debug );
        using var _ = Log0Logging.Use( factory );

        new Inventory().Check( "apple", "hunter2" );

        Assert.Equal( "Start Inventory.Check(item: apple, secret: ***)", factory.Messages[ 0 ] );
        Assert.Equal( "apple", factory.Entries[ 0 ].Properties[ "item" ] );
        Assert.DoesNotContain( factory.Entries, e => e.Message.Contains( "hunter2" ) );
    }

    [ Fact ]
    public void Debug_methods_are_silent_at_information()
    {
        var factory = new CapturingLoggerFactory();
        using var _ = Log0Logging.Use( factory );

        Assert.True( new Inventory().Check( "apple", "x" ) );

        // Only the nested Count, which logs at Information.
        Assert.Equal( [ "Start Inventory.Count", "Success Inventory.Count" ], factory.Messages );
    }

    [ Fact ]
    public void Static_extension_generic_and_internal_calls_are_intercepted()
    {
        var factory = new CapturingLoggerFactory();
        using var _ = Log0Logging.Use( factory );

        Assert.Equal( 6, Inventory.Total( 1, 2, 3 ) );
        Assert.Equal( "HI", "hi".Shout() );
        Assert.Equal( 2, new Box< string >().Map( "ab", s => s.Length ) );
        Assert.Equal( 6, new Inventory().CountTwice( "apple" ) );

        Assert.Equal(
            [
                "Start Inventory.Total", "Success Inventory.Total",
                "Start TextExtensions.Shout", "Success TextExtensions.Shout",
                "Start Box.Map", "Success Box.Map",
                "Start Inventory.Count", "Success Inventory.Count", "Start Inventory.Count", "Success Inventory.Count"
            ],
            factory.Messages
        );
    }

    [ Fact ]
    public void Calls_through_an_interface_are_logged_when_the_interface_method_has_Log()
    {
        var factory = new CapturingLoggerFactory();
        using var _ = Log0Logging.Use( factory );
        IGreeter greeter = new Greeter();

        Assert.Equal( "Hello Ada", greeter.Greet( "Ada" ) );

        Assert.Equal( [ "Start IGreeter.Greet", "Success IGreeter.Greet" ], factory.Messages );
        Assert.All( factory.Entries, e => Assert.Equal( "Log0.Tests.IGreeter", e.Category ) );
    }

    [ Fact ]
    public void Virtual_calls_still_dispatch_and_base_calls_are_not_logged_twice()
    {
        var factory = new CapturingLoggerFactory();
        using var _ = Log0Logging.Use( factory );
        Inventory inventory = new FruitInventory();

        Assert.Equal( "fruit inventory", inventory.Describe() );
        Assert.Equal( "fruit inventory", new FruitInventory().Describe() );

        Assert.Equal(
            [ "Start Inventory.Describe", "Success Inventory.Describe", "Start FruitInventory.Describe", "Success FruitInventory.Describe" ],
            factory.Messages
        );
    }

    [ Fact ]
    public void Calls_through_a_delegate_are_not_logged()
    {
        var factory = new CapturingLoggerFactory();
        using var _ = Log0Logging.Use( factory );

        // Documented limitation: only direct calls are rewritten.
        Func< string, int > count = new Inventory().Count;
        count( "apple" );

        Assert.Empty( factory.Messages );
    }

    #endregion
}
