using System.Collections;
using System.Globalization;
using System.Text;

namespace Log0;

/// <summary>
/// The structured state of one log entry: <c>Method</c>, one property per parameter and the
/// <c>{OriginalFormat}</c> template, the shape <c>LoggerMessage</c> produces.
/// </summary>
internal readonly struct Log0State(
    string format,
    string outcome,
    string method,
    string[ ]? keys,
    object?[ ]? values
) : IReadOnlyList< KeyValuePair< string, object? > >
{
    #region Constants

    private const int MAX_ITEMS = 20;

    #endregion

    #region Properties

    public static Func< Log0State, Exception?, string > Format { get; } = static ( state, _ ) => state.ToString();

    public int Count => 2 + ( keys?.Length ?? 0 );

    public KeyValuePair< string, object? > this[ int index ]
    {
        get
        {
            if ( index == 0 )
                return new( "Method", method );

            if ( index == Count - 1 )
                return new( "{OriginalFormat}", format );

            return new( keys![ index - 1 ], values![ index - 1 ] );
        }
    }

    #endregion

    #region Public Methods

    public IEnumerator< KeyValuePair< string, object? > > GetEnumerator()
    {
        for ( var i = 0; i < Count; i++ )
            yield return this[ i ];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString()
    {
        if ( keys is null )
            return outcome + " " + method;

        var builder = new StringBuilder( outcome ).Append( ' ' ).Append( method ).Append( '(' );

        for ( var i = 0; i < keys.Length; i++ )
        {
            if ( i > 0 )
                builder.Append( ", " );

            builder.Append( keys[ i ] ).Append( ": " );
            AppendValue( builder, values![ i ] );
        }

        return builder.Append( ')' ).ToString();
    }

    #endregion

    #region Private Methods

    private static void AppendValue( StringBuilder builder, object? value )
    {
        switch ( value )
        {
            case null:
                builder.Append( "null" );
                break;
            case string text:
                builder.Append( text );
                break;
            case IFormattable formattable:
                builder.Append( formattable.ToString( null, CultureInfo.InvariantCulture ) );
                break;
            case IEnumerable items:
                AppendItems( builder, items );
                break;
            default:
                builder.Append( value );
                break;
        }
    }

    private static void AppendItems( StringBuilder builder, IEnumerable items )
    {
        builder.Append( '[' );
        var count = 0;

        foreach ( var item in items )
        {
            if ( count > 0 )
                builder.Append( ", " );

            if ( count == MAX_ITEMS )
            {
                builder.Append( '…' );
                break;
            }

            AppendValue( builder, item );
            count++;
        }

        builder.Append( ']' );
    }

    #endregion
}
