namespace Log0.Tests;

// Plain [Log] methods: no partial, no Core. Calls to them are intercepted.

public class Inventory
{
    #region Fields

    private readonly Dictionary< string, int > _stock = new() { [ "apple" ] = 3 };

    #endregion

    #region Public Methods

    [ Log ]
    public int Count( string item ) => _stock.TryGetValue( item, out var count ) ? count : 0;

    [ Log ]
    public void Remove( string item )
    {
        if ( !_stock.Remove( item ) )
            throw new KeyNotFoundException( item );
    }

    [ Log ]
    public async Task< int > CountAsync( string item, CancellationToken cancellationToken )
    {
        await Task.Delay( 10, cancellationToken );

        return Count( item );
    }

    [ Log ]
    public async ValueTask RemoveAsync( string item )
    {
        await Task.Yield();
        Remove( item );
    }

    [ Log( LogLevel.Debug ) ]
    public bool Check( string item, [ NoLog ] string secret ) => secret.Length > 0 && Count( item ) > 0;

    [ Log ]
    public static int Total( params int[ ] counts ) => counts.Sum();

    [ Log ]
    public virtual string Describe() => "inventory";

    /// <summary>A call from inside the type is intercepted like any other.</summary>
    public int CountTwice( string item ) => Count( item ) + Count( item );

    #endregion
}

public sealed class FruitInventory : Inventory
{
    #region Public Methods

    public override string Describe() => "fruit " + base.Describe();

    #endregion
}

public interface IGreeter
{
    #region Public Methods

    [ Log ]
    string Greet( string name );

    #endregion
}

public sealed class Greeter : IGreeter
{
    #region Public Methods

    public string Greet( string name ) => "Hello " + name;

    #endregion
}

public class Box< T >
{
    #region Public Methods

    [ Log ]
    public TOut Map< TOut >( T value, Func< T, TOut > map ) => map( value );

    #endregion
}

public static class TextExtensions
{
    #region Public Methods

    [ Log ]
    public static string Shout( this string text ) => text.ToUpperInvariant();

    #endregion
}
