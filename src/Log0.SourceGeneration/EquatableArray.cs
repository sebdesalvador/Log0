using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Log0.SourceGeneration;

/// <summary>
/// An array with value equality, so a model holding one still caches correctly between runs.
/// A plain array compares by reference and would defeat the incremental pipeline.
/// </summary>
internal readonly struct EquatableArray< T >( T[ ] items ) : IEquatable< EquatableArray< T > >, IEnumerable< T >
    where T : IEquatable< T >
{
    private readonly T[ ] _items = items ?? [ ];

    public int Length => _items.Length;

    public bool Contains( T item ) => _items.Contains( item );

    public bool Equals( EquatableArray< T > other ) => _items.AsSpan().SequenceEqual( other._items.AsSpan() );

    public override bool Equals( object? obj ) => obj is EquatableArray< T > other && Equals( other );

    public override int GetHashCode()
    {
        var hash = 17;

        foreach ( var item in _items )
            hash = hash * 31 + ( item?.GetHashCode() ?? 0 );

        return hash;
    }

    public IEnumerator< T > GetEnumerator() => ( ( IEnumerable< T > )_items ).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    public static implicit operator EquatableArray< T >( T[ ] items ) => new( items );
}
