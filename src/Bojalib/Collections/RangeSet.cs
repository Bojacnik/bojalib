using System.Collections;
using System.Diagnostics;

namespace Bojalib.Collections;

/// <summary>A half-open range [<paramref name="Start"/>, <paramref name="End"/>): includes Start, excludes End.</summary>
/// <typeparam name="T">The endpoint type.</typeparam>
/// <param name="Start">The first point in the range.</param>
/// <param name="End">The first point after the range.</param>
public readonly record struct HalfOpenRange<T>(T Start, T End);

/// <summary>
/// A set of points stored as sorted, non-overlapping, non-adjacent half-open ranges, with range algebra: add or
/// remove whole ranges, test coverage and overlap, union, intersection and difference.
/// </summary>
/// <remarks>
/// <para>
/// Use it to represent <em>coverage</em>: booked time, IP address blocks, allocated address spans, permission or
/// feature windows. Ranges are kept canonical, so adding [1, 5) and [5, 9) stores the single range [1, 9), and
/// removing [3, 4) from it splits it into [1, 3) and [4, 9). (To index arbitrary overlapping intervals that carry
/// values, use <see cref="IntervalTree{TPoint, TValue}"/> instead.)
/// </para>
/// <para>
/// Ranges are half-open, [start, end), so adjacent ranges share an endpoint without overlapping and an empty range
/// (start = end) contains nothing; adding or removing an empty range does nothing. Points are compared only, never
/// stepped, so any ordered type works: integers (IPv4 addresses as <see cref="uint"/>), <see cref="DateTime"/>,
/// <see cref="decimal"/>, strings.
/// </para>
/// <para>
/// Backed by a <see cref="BTreeDictionary{TKey, TValue}"/> from start to end: <see cref="Add(T, T)"/> and
/// <see cref="Remove(T, T)"/> are O(log n) plus O(log n) per range they merge or delete, and point queries are O(log n).
/// Enumeration is in order and allocates nothing. Not thread-safe.
/// </para>
/// </remarks>
/// <typeparam name="T">The endpoint type.</typeparam>
[DebuggerDisplay("Count = {Count}")]
public sealed class RangeSet<T> : IReadOnlyCollection<HalfOpenRange<T>>
    where T : notnull
{
    private readonly BTreeDictionary<T, T> _ranges; // start -> end
    private readonly IComparer<T> _comparer;

    /// <summary>Creates an empty set.</summary>
    /// <param name="comparer">Orders the points; <see langword="null"/> for <see cref="Comparer{T}.Default"/>.</param>
    public RangeSet(IComparer<T>? comparer = null)
    {
        _ranges = new BTreeDictionary<T, T>(comparer);
        _comparer = _ranges.Comparer;
    }

    /// <summary>Creates a set covering <paramref name="ranges"/>, which may overlap and come in any order.</summary>
    /// <param name="ranges">The ranges.</param>
    /// <param name="comparer">Orders the points; <see langword="null"/> for <see cref="Comparer{T}.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ranges"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A range ends before it starts.</exception>
    public RangeSet(IEnumerable<HalfOpenRange<T>> ranges, IComparer<T>? comparer = null)
        : this(comparer)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        foreach (HalfOpenRange<T> range in ranges)
        {
            Add(range.Start, range.End);
        }
    }

    /// <summary>Gets the number of disjoint ranges (not the number of points).</summary>
    public int Count => _ranges.Count;

    /// <summary>Gets whether the set is empty.</summary>
    public bool IsEmpty => _ranges.Count == 0;

    /// <summary>Gets the comparer that orders the points.</summary>
    public IComparer<T> Comparer => _comparer;

    /// <summary>Gets the smallest range that covers the whole set: [first start, last end).</summary>
    /// <exception cref="InvalidOperationException">The set is empty.</exception>
    public HalfOpenRange<T> Bounds
    {
        get
        {
            if (_ranges.Count == 0)
            {
                ThrowHelper.ThrowEmptyCollection("range set");
            }

            return new(_ranges.First.Key, _ranges.Last.Value);
        }
    }

    /// <summary>Adds the points in [<paramref name="start"/>, <paramref name="end"/>), merging with overlapping or adjacent ranges.</summary>
    /// <param name="start">The first point.</param>
    /// <param name="end">The first point after the range.</param>
    /// <returns><see langword="true"/> if any point was added.</returns>
    /// <exception cref="ArgumentException"><paramref name="end"/> is less than <paramref name="start"/>.</exception>
    public bool Add(T start, T end)
    {
        if (!CheckRange(start, end))
        {
            return false;
        }

        // Extend left over a range that overlaps or touches the start.
        if (_ranges.TryGetFloor(start, out KeyValuePair<T, T> floor) && _comparer.Compare(floor.Value, start) >= 0)
        {
            if (_comparer.Compare(floor.Value, end) >= 0)
            {
                return false; // already covered
            }

            start = floor.Key;
        }

        // Swallow every range that starts inside or right at the end of the new one.
        while (_ranges.TryGetCeiling(start, out KeyValuePair<T, T> next) && _comparer.Compare(next.Key, end) <= 0)
        {
            if (_comparer.Compare(next.Value, end) > 0)
            {
                end = next.Value;
            }

            _ranges.Remove(next.Key);
        }

        _ranges.Add(start, end);
        return true;
    }

    /// <summary>Adds a range; see <see cref="Add(T, T)"/>.</summary>
    /// <param name="range">The range.</param>
    /// <returns><see langword="true"/> if any point was added.</returns>
    /// <exception cref="ArgumentException">The range ends before it starts.</exception>
    public bool Add(HalfOpenRange<T> range) => Add(range.Start, range.End);

    /// <summary>Removes the points in [<paramref name="start"/>, <paramref name="end"/>), splitting a range that covers both sides.</summary>
    /// <param name="start">The first point.</param>
    /// <param name="end">The first point after the range.</param>
    /// <returns><see langword="true"/> if any point was removed.</returns>
    /// <exception cref="ArgumentException"><paramref name="end"/> is less than <paramref name="start"/>.</exception>
    public bool Remove(T start, T end)
    {
        if (!CheckRange(start, end))
        {
            return false;
        }

        bool removed = false;

        // A range that starts before the removal and reaches into it keeps its left part (and its right part, if any).
        if (_ranges.TryGetFloor(start, out KeyValuePair<T, T> floor) &&
            _comparer.Compare(floor.Key, start) < 0 && _comparer.Compare(floor.Value, start) > 0)
        {
            _ranges[floor.Key] = start;
            if (_comparer.Compare(floor.Value, end) > 0)
            {
                _ranges.Add(end, floor.Value);
                return true;
            }

            removed = true;
        }

        // Ranges that start inside the removal are deleted; the last one may keep a right part.
        while (_ranges.TryGetCeiling(start, out KeyValuePair<T, T> next) && _comparer.Compare(next.Key, end) < 0)
        {
            _ranges.Remove(next.Key);
            if (_comparer.Compare(next.Value, end) > 0)
            {
                _ranges.Add(end, next.Value);
            }

            removed = true;
        }

        return removed;
    }

    /// <summary>Removes a range; see <see cref="Remove(T, T)"/>.</summary>
    /// <param name="range">The range.</param>
    /// <returns><see langword="true"/> if any point was removed.</returns>
    /// <exception cref="ArgumentException">The range ends before it starts.</exception>
    public bool Remove(HalfOpenRange<T> range) => Remove(range.Start, range.End);

    /// <summary>Returns whether <paramref name="point"/> is in the set.</summary>
    /// <param name="point">The point.</param>
    /// <returns><see langword="true"/> if a range contains it.</returns>
    public bool Contains(T point) =>
        _ranges.TryGetFloor(point, out KeyValuePair<T, T> floor) && _comparer.Compare(floor.Value, point) > 0;

    /// <summary>
    /// Returns whether every point in [<paramref name="start"/>, <paramref name="end"/>) is in the set. An empty range is
    /// always contained.
    /// </summary>
    /// <param name="start">The first point.</param>
    /// <param name="end">The first point after the range.</param>
    /// <returns><see langword="true"/> if a single stored range covers it.</returns>
    /// <exception cref="ArgumentException"><paramref name="end"/> is less than <paramref name="start"/>.</exception>
    public bool Contains(T start, T end) =>
        !CheckRange(start, end) ||
        (_ranges.TryGetFloor(start, out KeyValuePair<T, T> floor) && _comparer.Compare(floor.Value, end) >= 0);

    /// <summary>Returns whether any point in [<paramref name="start"/>, <paramref name="end"/>) is in the set. An empty range overlaps nothing.</summary>
    /// <param name="start">The first point.</param>
    /// <param name="end">The first point after the range.</param>
    /// <returns><see langword="true"/> if some stored range intersects it.</returns>
    /// <exception cref="ArgumentException"><paramref name="end"/> is less than <paramref name="start"/>.</exception>
    public bool Overlaps(T start, T end)
    {
        if (!CheckRange(start, end))
        {
            return false;
        }

        return (_ranges.TryGetFloor(start, out KeyValuePair<T, T> floor) && _comparer.Compare(floor.Value, start) > 0) ||
            (_ranges.TryGetCeiling(start, out KeyValuePair<T, T> next) && _comparer.Compare(next.Key, end) < 0);
    }

    /// <summary>Gets the stored range that contains <paramref name="point"/>.</summary>
    /// <param name="point">The point.</param>
    /// <param name="range">The maximal range around it.</param>
    /// <returns><see langword="true"/> if the point is in the set.</returns>
    public bool TryGetRange(T point, out HalfOpenRange<T> range)
    {
        if (_ranges.TryGetFloor(point, out KeyValuePair<T, T> floor) && _comparer.Compare(floor.Value, point) > 0)
        {
            range = new(floor.Key, floor.Value);
            return true;
        }

        range = default;
        return false;
    }

    /// <summary>Adds every point of <paramref name="other"/>.</summary>
    /// <param name="other">Another set with the same ordering.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public void UnionWith(RangeSet<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(other, this))
        {
            return;
        }

        foreach (HalfOpenRange<T> range in other)
        {
            Add(range.Start, range.End);
        }
    }

    /// <summary>Removes every point of <paramref name="other"/>.</summary>
    /// <param name="other">Another set with the same ordering.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public void ExceptWith(RangeSet<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(other, this))
        {
            Clear();
            return;
        }

        foreach (HalfOpenRange<T> range in other)
        {
            Remove(range.Start, range.End);
        }
    }

    /// <summary>Keeps only the points that are also in <paramref name="other"/>, in O(n + m).</summary>
    /// <param name="other">Another set with the same ordering.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public void IntersectWith(RangeSet<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(other, this))
        {
            return;
        }

        // Two-pointer sweep over both sorted lists; results come out sorted and disjoint.
        var result = new List<HalfOpenRange<T>>();
        BTreeDictionary<T, T>.Enumerator a = _ranges.GetEnumerator();
        BTreeDictionary<T, T>.Enumerator b = other._ranges.GetEnumerator();
        bool hasA = a.MoveNext();
        bool hasB = b.MoveNext();
        while (hasA && hasB)
        {
            KeyValuePair<T, T> x = a.Current;
            KeyValuePair<T, T> y = b.Current;
            T start = _comparer.Compare(x.Key, y.Key) >= 0 ? x.Key : y.Key;
            int endOrder = _comparer.Compare(x.Value, y.Value);
            T end = endOrder <= 0 ? x.Value : y.Value;
            if (_comparer.Compare(start, end) < 0)
            {
                result.Add(new(start, end));
            }

            if (endOrder <= 0)
            {
                hasA = a.MoveNext();
            }

            if (endOrder >= 0)
            {
                hasB = b.MoveNext();
            }
        }

        _ranges.Clear();
        foreach (HalfOpenRange<T> range in result)
        {
            _ranges.Add(range.Start, range.End);
        }
    }

    /// <summary>Returns whether both sets contain exactly the same points.</summary>
    /// <param name="other">Another set with the same ordering.</param>
    /// <returns><see langword="true"/> if equal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public bool SetEquals(RangeSet<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.Count != Count)
        {
            return false;
        }

        BTreeDictionary<T, T>.Enumerator b = other._ranges.GetEnumerator();
        foreach (KeyValuePair<T, T> x in _ranges)
        {
            b.MoveNext();
            if (_comparer.Compare(x.Key, b.Current.Key) != 0 || _comparer.Compare(x.Value, b.Current.Value) != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Removes every range.</summary>
    public void Clear() => _ranges.Clear();

    /// <summary>Enumerates the stored ranges in ascending order.</summary>
    /// <returns>An allocation-free enumerator.</returns>
    public Enumerator GetEnumerator() => new(_ranges.GetEnumerator());

    IEnumerator<HalfOpenRange<T>> IEnumerable<HalfOpenRange<T>>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // Returns false for an empty range; throws for a reversed one.
    private bool CheckRange(T start, T end)
    {
        ThrowHelper.ThrowIfNull(start);
        ThrowHelper.ThrowIfNull(end);
        int order = _comparer.Compare(start, end);
        if (order > 0)
        {
            throw new ArgumentException("The range ends before it starts.", nameof(end));
        }

        return order < 0;
    }

    /// <summary>Enumerates a <see cref="RangeSet{T}"/>.</summary>
    public struct Enumerator : IEnumerator<HalfOpenRange<T>>
    {
        private BTreeDictionary<T, T>.Enumerator _inner;

        internal Enumerator(BTreeDictionary<T, T>.Enumerator inner) => _inner = inner;

        /// <summary>Gets the current range.</summary>
        public readonly HalfOpenRange<T> Current => new(_inner.Current.Key, _inner.Current.Value);

        readonly object IEnumerator.Current => Current;

        /// <summary>Advances to the next range.</summary>
        /// <returns><see langword="false"/> at the end.</returns>
        public bool MoveNext() => _inner.MoveNext();

        /// <summary>Restarts the enumeration.</summary>
        public void Reset() => _inner.Reset();

        /// <summary>Does nothing.</summary>
        public readonly void Dispose()
        {
        }
    }
}
