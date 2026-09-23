namespace Log0.Tests;

public sealed class LevelAndParameterTests
{
    #region Public Methods

    [ Fact ]
    public void Parameters_are_not_logged_at_information()
    {
        var factory = new CapturingLoggerFactory( LogLevel.Information );
        using var _ = Log0Logging.Use( factory );

        new Calculator().Add( 1, 2 );

        Assert.Equal( "Start Calculator.Add", factory.Messages[ 0 ] );
        Assert.False( factory.Entries[ 0 ].Properties.ContainsKey( "a" ) );
    }

    [ Theory ]
    [ InlineData( LogLevel.Debug ) ]
    [ InlineData( LogLevel.Trace ) ]
    public void Parameters_are_logged_when_debug_or_lower_is_enabled( LogLevel minimum )
    {
        var factory = new CapturingLoggerFactory( minimum );
        using var _ = Log0Logging.Use( factory );

        new Calculator().Add( 1, 2 );

        var start = factory.Entries[ 0 ];
        Assert.Equal( "Start Calculator.Add(a: 1, b: 2)", start.Message );
        Assert.Equal( 1, start.Properties[ "a" ] );
        Assert.Equal( 2, start.Properties[ "b" ] );
        Assert.Equal( "Start {Method}(a: {a}, b: {b})", start.Properties[ "{OriginalFormat}" ] );
        Assert.Equal( "Success Calculator.Add", factory.Messages[ 1 ] );
    }

    [ Fact ]
    public void Debug_level_methods_are_silent_at_information()
    {
        var factory = new CapturingLoggerFactory( LogLevel.Information );
        using var _ = Log0Logging.Use( factory );

        new Calculator().Quiet( 1 );

        Assert.Empty( factory.Entries );
    }

    [ Fact ]
    public void Debug_level_methods_log_at_debug()
    {
        var factory = new CapturingLoggerFactory( LogLevel.Debug );
        using var _ = Log0Logging.Use( factory );

        new Calculator().Quiet( 7 );

        Assert.Equal( [ "Start Calculator.Quiet(x: 7)", "Success Calculator.Quiet" ], factory.Messages );
        Assert.All( factory.Entries, e => Assert.Equal( LogLevel.Debug, e.Level ) );
    }

    [ Fact ]
    public void Errors_are_logged_even_when_the_method_level_is_disabled()
    {
        var factory = new CapturingLoggerFactory( LogLevel.Information );
        using var _ = Log0Logging.Use( factory );

        Assert.Throws< InvalidOperationException >( () => new Calculator().QuietFail() );

        var entry = Assert.Single( factory.Entries );
        Assert.Equal( LogLevel.Error, entry.Level );
        Assert.Equal( "Error Calculator.QuietFail", entry.Message );
    }

    [ Fact ]
    public void Cancellation_tokens_and_ref_structs_are_left_out()
    {
        var factory = new CapturingLoggerFactory( LogLevel.Debug );
        using var _ = Log0Logging.Use( factory );

        new Calculator().Length( "abc".AsSpan(), "label" );

        Assert.Equal( "Start Calculator.Length(label: label)", factory.Messages[ 0 ] );
    }

    [ Fact ]
    public void Values_are_formatted_readably()
    {
        var factory = new CapturingLoggerFactory( LogLevel.Debug );
        using var _ = Log0Logging.Use( factory );

        new Calculator().Describe( null, [ 1, 2, 3 ], new DateTime( 2026, 9, 23, 14, 0, 0, DateTimeKind.Utc ) );
        new Calculator().Sum( 4, 5 );

        Assert.Equal( "Start Calculator.Describe(text: null, numbers: [1, 2, 3], when: 09/23/2026 14:00:00)", factory.Messages[ 0 ] );
        Assert.Equal( "Start Calculator.Sum(values: [4, 5])", factory.Messages[ 2 ] );
    }

    [ Fact ]
    public void NoLog_parameter_is_masked_and_never_read()
    {
        var factory = new CapturingLoggerFactory( LogLevel.Debug );
        using var _ = Log0Logging.Use( factory );

        new Vault().Login( "seb", "secret" );

        Assert.Equal( "Start Vault.Login(user: seb, password: ***)", factory.Messages[ 0 ] );
        Assert.Equal( "***", factory.Entries[ 0 ].Properties[ "password" ] );
        Assert.DoesNotContain( factory.Entries, e => e.Message.Contains( "secret" ) );
    }

    [ Fact ]
    public void NoLog_type_masks_every_parameter_of_that_type()
    {
        var factory = new CapturingLoggerFactory( LogLevel.Debug );
        using var _ = Log0Logging.Use( factory );

        new Vault().Check( new Credentials( "seb", "pw" ), new ApiKey( "k-123" ) );

        Assert.Equal( "Start Vault.Check(credentials: Credentials { User = seb, Password = pw }, key: ***)", factory.Messages[ 0 ] );
    }

    [ Fact ]
    public void NoLog_method_logs_outcomes_but_no_parameters()
    {
        var factory = new CapturingLoggerFactory( LogLevel.Trace );
        using var _ = Log0Logging.Use( factory );

        new Vault().Store( "top secret", 3 );

        Assert.Equal( [ "Start Vault.Store", "Success Vault.Store" ], factory.Messages );
    }

    #endregion
}
