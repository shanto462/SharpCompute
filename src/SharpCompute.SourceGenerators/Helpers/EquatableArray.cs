using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace SharpCompute.SourceGenerators;

/// <summary>
/// An immutable array with structural equality, so pipeline models compare by value and the
/// incremental generator can cache its outputs.
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    public static readonly EquatableArray<T> Empty = new(Array.Empty<T>());

    private readonly T[]? _items;

    public EquatableArray(T[] items) => _items = items;

    public EquatableArray(IEnumerable<T> items) => _items = items.ToArray();

    public int Count => _items?.Length ?? 0;

    public T this[int index] => _items![index];

    public bool Equals(EquatableArray<T> other)
    {
        T[] left = _items ?? Array.Empty<T>();
        T[] right = other._items ?? Array.Empty<T>();

        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;

            foreach (T item in _items ?? Array.Empty<T>())
            {
                hash = (hash * 31) + (item is null ? 0 : item.GetHashCode());
            }

            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_items ?? Array.Empty<T>())).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);
}
