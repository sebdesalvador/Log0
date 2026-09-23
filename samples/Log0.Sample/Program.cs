using Log0.Sample;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder( new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory } );
builder.Services.AddLog0();
builder.Services.AddSingleton< OrderService >();

// `dotnet run -- --debug` lowers the level to Debug: parameter values appear, and so do
// [Log(LogLevel.Debug)] methods.
if ( args.Contains( "--debug" ) )
    builder.Logging.AddFilter( "Log0.Sample", LogLevel.Debug );

using var host = builder.Build();
await host.StartAsync();

var orders = host.Services.GetRequiredService< OrderService >();

// Start + Success
var order = await orders.GetOrder( 1, CancellationToken.None );
orders.IsValid( order );
await orders.Pay( order.Id, new CardNumber( "4111 1111 1111 1111" ), "123" );

// Start + Error, with the exception
try
{
    await orders.GetOrder( 99, CancellationToken.None );
}
catch ( KeyNotFoundException )
{
}

// Start + Canceled (warning)
try
{
    await orders.GetOrder( 2, new CancellationTokenSource( TimeSpan.FromMilliseconds( 1 ) ).Token );
}
catch ( OperationCanceledException )
{
}

await host.StopAsync();
