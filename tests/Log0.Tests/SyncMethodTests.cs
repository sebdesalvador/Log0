namespace Log0.Tests;

public sealed class SyncMethodTests
{
    #region Public Methods

    [ Fact ]
    public void Success_logs_start_then_success_at_information()
    {
        var factory = new CapturingLoggerFactory();
        using var _ = Log0Logging.Use( factory );

        var result = new Calculator().Add( 1, 2 );

        Assert.Equal( 3, result );
        Assert.Equal( [ "Start Calculator.Add", "Success Calculator.Add" ], factory.Messages );
        Assert.All( factory.Entries, e => Assert.Equal( LogLevel.Information, e.Level ) );
        Assert.Equal( "Calculator.Add", factory.Entries[ 0 ].Properties[ "Method" ] );
    }

    [ Fact ]
    public void Category_is_the_full_type_name_like_ILogger_of_T()
    {
        var factory = new CapturingLoggerFactory();
        using var _ = Log0Logging.Use( factory );

        new Calculator().Add( 1, 2 );

        Assert.All( factory.Entries, e => Assert.Equal( "Log0.Tests.Calculator", e.Category ) );
    }

    [ Fact ]
    public void Event_ids_distinguish_the_four_outcomes()
    {
        var factory = new CapturingLoggerFactory();
        using var _ = Log0Logging.Use( factory );

        new Calculator().Add( 1, 2 );
        Assert.Throws< InvalidOperationException >( () => new Calculator().Fail( "x" ) );
        Assert.Throws< OperationCanceledException >( () => new Calculator().Cancel() );

        Assert.Equal(
            [ "Start", "Success", "Start", "Error", "Start", "Canceled" ],
            factory.Entries.Select( e => e.EventId.Name )
        );
    }

    [ Fact ]
    public void Exception_logs_error_with_the_exception_and_rethrows_the_same_instance()
    {
        var factory = new CapturingLoggerFactory();
        using var _ = Log0Logging.Use( factory );

        var thrown = Assert.Throws< InvalidOperationException >( () => new Calculator().Fail( "boom" ) );

        Assert.Equal( [ "Start Calculator.Fail", "Error Calculator.Fail" ], factory.Messages );
        var error = factory.Entries[ 1 ];
        Assert.Equal( LogLevel.Error, error.Level );
        Assert.Same( thrown, error.Exception );
        Assert.Contains( "Calculator.Fail", thrown.StackTrace );
    }

    [ Fact ]
    public void Cancellation_logs_canceled_as_a_warning()
    {
        var factory = new CapturingLoggerFactory();
        using var _ = Log0Logging.Use( factory );

        Assert.Throws< OperationCanceledException >( () => new Calculator().Cancel() );

        Assert.Equal( [ "Start Calculator.Cancel", "Canceled Calculator.Cancel" ], factory.Messages );
        Assert.Equal( LogLevel.Warning, factory.Entries[ 1 ].Level );
    }

    [ Fact ]
    public void Struct_and_nullable_struct_returns_pass_through()
    {
        var factory = new CapturingLoggerFactory();
        using var _ = Log0Logging.Use( factory );
        var calculator = new Calculator();

        Assert.Equal( new Money( 5, "USD" ), calculator.Convert( new Money( 5, "EUR" ), "USD" ) );
        Assert.Null( calculator.Find( -1 ) );
        Assert.Equal( 4, calculator.Find( 4 ) );
        Assert.Equal( 6, factory.Entries.Count );
    }

    [ Fact ]
    public void Static_generic_nested_and_generic_type_methods_are_logged()
    {
        var factory = new CapturingLoggerFactory();
        using var _ = Log0Logging.Use( factory );

        Assert.Equal( 8, Calculator.Twice( 4 ) );
        Assert.Equal( "x", new Calculator().Echo( "x" ) );
        Assert.Equal( "Hello Ada", new Outer.Inner().Hello( "Ada" ) );
        Assert.Null( new Repository< string >().Load( 1 ) );

        Assert.Equal(
            [
                "Start Calculator.Twice", "Success Calculator.Twice",
                "Start Calculator.Echo", "Success Calculator.Echo",
                "Start Outer.Inner.Hello", "Success Outer.Inner.Hello",
                "Start Repository.Load", "Success Repository.Load"
            ],
            factory.Messages
        );
    }

    [ Fact ]
    public void Ref_and_out_parameters_flow_through_to_the_implementation()
    {
        var factory = new CapturingLoggerFactory( LogLevel.Debug );
        using var _ = Log0Logging.Use( factory );
        var calculator = new Calculator();

        var counter = 41;
        calculator.Increment( ref counter );
        var parsed = calculator.TryParse( "12", out var value );

        Assert.Equal( 42, counter );
        Assert.True( parsed );
        Assert.Equal( 12, value );
        Assert.Contains( "Start Calculator.Increment(counter: 41)", factory.Messages );
        // An out parameter has no value yet when the method starts.
        Assert.Contains( "Start Calculator.TryParse(text: 12)", factory.Messages );
    }

    [ Fact ]
    public void Nothing_is_logged_and_nothing_breaks_without_a_logger_factory()
    {
        using var _ = Log0Logging.Use( null );

        Assert.Equal( 3, new Calculator().Add( 1, 2 ) );
        Assert.Throws< InvalidOperationException >( () => new Calculator().Fail( "x" ) );
    }

    #endregion
}
