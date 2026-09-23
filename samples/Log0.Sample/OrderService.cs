using Log0;
using Microsoft.Extensions.Logging;

namespace Log0.Sample;

public sealed record Order( int Id, string Customer, decimal Total );

/// <summary>Never logged: every parameter of this type shows as ***.</summary>
[ NoLog ]
public sealed record CardNumber( string Value );

/// <summary>
/// No logger field, no constructor parameter: [Log] methods get their logger from the app's
/// ILoggerFactory, handed over by services.AddLog0().
/// </summary>
public sealed partial class OrderService
{
    private readonly Dictionary< int, Order > _orders = new()
    {
        [ 1 ] = new Order( 1, "Ada", 42.50m ),
        [ 2 ] = new Order( 2, "Grace", 17.00m )
    };

    [ Log ]
    public partial Task< Order > GetOrder( int id, CancellationToken cancellationToken );

    private async Task< Order > GetOrderCore( int id, CancellationToken cancellationToken )
    {
        await Task.Delay( 50, cancellationToken );

        return _orders.TryGetValue( id, out var order )
            ? order
            : throw new KeyNotFoundException( $"Order {id} does not exist" );
    }

    [ Log ]
    public partial ValueTask< decimal > Pay( int orderId, CardNumber card, [ NoLog ] string cvv );

    private ValueTask< decimal > PayCore( int orderId, CardNumber card, string cvv )
        => new( _orders[ orderId ].Total );

    // Debug: only shows up when the category's level is Debug or lower.
    [ Log( LogLevel.Debug ) ]
    public partial bool IsValid( Order order );

    private bool IsValidCore( Order order ) => order.Total > 0;
}
