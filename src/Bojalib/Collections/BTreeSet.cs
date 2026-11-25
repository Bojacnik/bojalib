using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Bojalib.Collections;

/// <summary>Creates <see cref="BTreeSet{T}"/> instances; also enables collection expressions.</summary>
public static class BTreeSet
{
    /// <summary>Creates a set holding <paramref name="items"/> (duplicates are ignored).</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="items">The items.</param>
    /// <returns>The set.</returns>
    public static BTreeSet<T> Create<T>(ReadOnlySpan<T> items)
        where T : notnull
    {
        var set = new BTreeSet<T>();
        foreach (T item in items)
        {
            set.Add(item);
        }

        return set;
    }
}

/// <summary>
/// A sorted set backed by a B+tree that also knows positions: O(log n) add, remove and lookup, plus
/// <c>set[i]</c> (the i-th smallest element), <see cref="IndexOf"/>, <see cref="CountLessThan"/>, floor/ceiling and
/// range enumeration — which <see cref="SortedSet{T}"/> can't do in O(log n). See
/// <see cref="BTreeDictionary{TKey, TValue}"/> for the design.
/// </summary>
/// <remarks>Typical uses: leaderboards ("what rank is this score?"), percentiles over a changing set, ordered indexes.</remarks>
/// <typeparam name="T">The element type.</typeparam>
[DebuggerDisplay("Count = {Count}")]
[CollectionBuilder(typeof(BTreeSet), nameof(BTreeSet.Create))]
public sealed class BTreeSet<T> : ISet<T>, IReadOnlySet<T>, IReadOnlyList<T>
    where T : notnull
{
    private readonly BTreeDictionary<T, NoValue> _tree;

    /// <summary>Creates an empty set.</summary>
    /// <param name="comparer">Orders the elements; <see langword="null"/> for <see cref="Comparer{T}.Default"/>.</param>
    public BTreeSet(IComparer<T>? comparer = null) => _tree = new BTreeDictionary<T, NoValue>(comparer);

    /// <summary>Gets the number of elements.</summary>
    public int Count => _tree.Count;

    /// <summary>Gets the comparer that orders the elements.</summary>
    public IComparer<T> Comparer => _tree.Comparer;

    /// <summary>Gets the smallest element.</summary>
    /// <exception cref="InvalidOperationException">The set is empty.</exception>
    public T Min => _tree.First.Key;

    /// <summary>Gets the largest element.</summary>
    /// <exception cref="InvalidOperationException">The set is empty.</exception>
    public T Max => _tree.Last.Key;

    /// <summary>Gets the element at <paramref name="index"/> in sorted order (0 = smallest). O(log n).</summary>
    /// <param name="index">The position.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside [0, Count).</exception>
    public T this[int index] => _tree.GetAt(index).Key;

    /// <summary>Adds <paramref name="item"/>.</summary>
    /// <param name="item">The element.</param>
    /// <returns><see langword="true"/> if added; <see langword="false"/> if already present.</returns>
    public bool Add(T item) => _tree.TryAdd(item, default);

    /// <summary>Removes <paramref name="item"/>.</summary>
    /// <param name="item">The element.</param>
    /// <returns><see langword="true"/> if it was present.</returns>
    public bool Remove(T item) => _tree.Remove(item);

    /// <summary>Removes the element at <paramref name="index"/> in sorted order. O(log n).</summary>
    /// <param name="index">The position.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside [0, Count).</exception>
    public void RemoveAt(int index) => _tree.RemoveAt(index);

    /// <summary>Returns whether <paramref name="item"/> is present.</summary>
    /// <param name="item">The element.</param>
    /// <returns><see langword="true"/> if present.</returns>
    public bool Contains(T item) => _tree.ContainsKey(item);

    /// <summary>Returns the position of <paramref name="item"/> in sorted order, or -1 if absent. O(log n).</summary>
    /// <param name="item">The element.</param>
    /// <returns>The index, or -1.</returns>
    public int IndexOf(T item) => _tree.IndexOfKey(item);

    /// <summary>Returns how many elements are less than <paramref name="item"/> (its rank), present or not. O(log n).</summary>
    /// <param name="item">The value.</param>
    /// <returns>The number of smaller elements.</returns>
    public int CountLessThan(T item) => _tree.CountLessThan(item);

    /// <summary>Gets the largest element &lt;= <paramref name="item"/>.</summary>
    /// <param name="item">The value.</param>
    /// <param name="floor">The element.</param>
    /// <returns><see langword="true"/> if there is one.</returns>
    public bool TryGetFloor(T item, out T floor)
    {
        bool found = _tree.TryGetFloor(item, out KeyValuePair<T, NoValue> entry);
        floor = entry.Key;
        return found;
    }

    /// <summary>Gets the smallest element &gt;= <paramref name="item"/>.</summary>
    /// <param name="item">The value.</param>
    /// <param name="ceiling">The element.</param>
    /// <returns><see langword="true"/> if there is one.</returns>
    public bool TryGetCeiling(T item, out T ceiling)
    {
        bool found = _tree.TryGetCeiling(item, out KeyValuePair<T, NoValue> entry);
        ceiling = entry.Key;
        return found;
    }

    /// <summary>Enumerates the elements in [<paramref name="from"/>, <paramref name="to"/>], in order.</summary>
    /// <param name="from">The smallest value to include.</param>
    /// <param name="to">The largest value to include.</param>
    /// <returns>An allocation-free enumerable.</returns>
    public RangeEnumerable EnumerateRange(T from, T to) => new(_tree.EnumerateRange(from, to));

    /// <summary>Removes all elements.</summary>
    public void Clear() => _tree.Clear();

    /// <summary>Copies the elements, in sorted order, to <paramref name="array"/>.</summary>
    /// <param name="array">The destination.</param>
    /// <param name="arrayIndex">Where to start writing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="array"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="arrayIndex"/> is negative.</exception>
    /// <exception cref="ArgumentException">The array is too short.</exception>
    public void CopyTo(T[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
        if (array.Length - arrayIndex < Count)
        {
            throw new ArgumentException("The destination array is too short.", nameof(array));
        }

        foreach (T item in this)
        {
            array[arrayIndex++] = item;
        }
    }

    /// <summary>Adds every element of <paramref name="other"/>.</summary>
    /// <param name="other">The elements to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public void UnionWith(IEnumerable<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(other, this))
        {
            return;
        }

        foreach (T item in other)
        {
            Add(item);
        }
    }

    /// <summary>Removes every element of <paramref name="other"/>.</summary>
    /// <param name="other">The elements to remove.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public void ExceptWith(IEnumerable<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(other, this))
        {
            Clear();
            return;
        }

        foreach (T item in other)
        {
            Remove(item);
        }
    }

    /// <summary>Keeps only the elements that are also in <paramref name="other"/>.</summary>
    /// <param name="other">The elements to keep.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public void IntersectWith(IEnumerable<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(other, this) || Count == 0)
        {
            return;
        }

        BTreeSet<T> keep = AsSet(other);
        var drop = new List<T>();
        foreach (T item in this)
        {
            if (!keep.Contains(item))
            {
                drop.Add(item);
            }
        }

        foreach (T item in drop)
        {
            Remove(item);
        }
    }

    /// <summary>Keeps the elements that are in exactly one of this set and <paramref name="other"/>.</summary>
    /// <param name="other">The other elements.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public void SymmetricExceptWith(IEnumerable<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(other, this))
        {
            Clear();
            return;
        }

        foreach (T item in AsSet(other))
        {
            if (!Remove(item))
            {
                Add(item);
            }
        }
    }

    /// <summary>Returns whether every element of this set is in <paramref name="other"/>.</summary>
    /// <param name="other">The other elements.</param>
    /// <returns><see langword="true"/> if this set is a subset.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public bool IsSubsetOf(IEnumerable<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        BTreeSet<T> set = AsSet(other);
        return Count <= set.Count && ContainedIn(set);
    }

    /// <summary>Returns whether this set is a subset of <paramref name="other"/> and smaller than it.</summary>
    /// <param name="other">The other elements.</param>
    /// <returns><see langword="true"/> if this set is a proper subset.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public bool IsProperSubsetOf(IEnumerable<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        BTreeSet<T> set = AsSet(other);
        return Count < set.Count && ContainedIn(set);
    }

    /// <summary>Returns whether every element of <paramref name="other"/> is in this set.</summary>
    /// <param name="other">The other elements.</param>
    /// <returns><see langword="true"/> if this set is a superset.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public bool IsSupersetOf(IEnumerable<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach (T item in other)
        {
            if (!Contains(item))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Returns whether this set is a superset of <paramref name="other"/> and larger than it.</summary>
    /// <param name="other">The other elements.</param>
    /// <returns><see langword="true"/> if this set is a proper superset.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public bool IsProperSupersetOf(IEnumerable<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        BTreeSet<T> set = AsSet(other);
        return Count > set.Count && set.ContainedIn(this);
    }

    /// <summary>Returns whether this set and <paramref name="other"/> share at least one element.</summary>
    /// <param name="other">The other elements.</param>
    /// <returns><see langword="true"/> if they overlap.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public bool Overlaps(IEnumerable<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach (T item in other)
        {
            if (Contains(item))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns whether this set and <paramref name="other"/> contain the same elements (duplicates in <paramref name="other"/> are ignored).</summary>
    /// <param name="other">The other elements.</param>
    /// <returns><see langword="true"/> if they are equal as sets.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public bool SetEquals(IEnumerable<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        BTreeSet<T> set = AsSet(other);
        return Count == set.Count && ContainedIn(set);
    }

    void ICollection<T>.Add(T item) => Add(item);

    bool ICollection<T>.IsReadOnly => false;

    // Other elements as a set with this set's ordering, so membership means the same on both sides.
    private BTreeSet<T> AsSet(IEnumerable<T> other)
    {
        if (other is BTreeSet<T> set && set.Comparer.Equals(Comparer))
        {
            return set;
        }

        var copy = new BTreeSet<T>(Comparer);
        foreach (T item in other)
        {
            copy.Add(item);
        }

        return copy;
    }

    private bool ContainedIn(BTreeSet<T> other)
    {
        foreach (T item in this)
        {
            if (!other.Contains(item))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Enumerates the elements in sorted order.</summary>
    /// <returns>An allocation-free enumerator.</returns>
    public Enumerator GetEnumerator() => new(_tree.GetEnumerator());

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal void Validate() => _tree.Validate();

    /// <summary>Elements in a closed range. See <see cref="EnumerateRange"/>.</summary>
    public readonly struct RangeEnumerable : IEnumerable<T>
    {
        private readonly BTreeDictionary<T, NoValue>.RangeEnumerable _inner;

        internal RangeEnumerable(BTreeDictionary<T, NoValue>.RangeEnumerable inner) => _inner = inner;

        /// <summary>Returns the enumerator.</summary>
        /// <returns>An allocation-free enumerator.</returns>
        public Enumerator GetEnumerator() => new(_inner.GetEnumerator());

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Enumerates a <see cref="BTreeSet{T}"/> in sorted order.</summary>
    public struct Enumerator : IEnumerator<T>
    {
        private BTreeDictionary<T, NoValue>.Enumerator _inner;

        internal Enumerator(BTreeDictionary<T, NoValue>.Enumerator inner) => _inner = inner;

        /// <inheritdoc />
        public readonly T Current => _inner.Current.Key;

        readonly object IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext() => _inner.MoveNext();

        /// <inheritdoc />
        public void Reset() => _inner.Reset();

        /// <inheritdoc />
        public readonly void Dispose()
        {
        }
    }
}

/// <summary>A zero-information value for sets built on dictionaries.</summary>
internal readonly struct NoValue;
