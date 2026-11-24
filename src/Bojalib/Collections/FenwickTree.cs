using System.Diagnostics;
using System.Numerics;

namespace Bojalib.Collections;

/// <summary>
/// A fixed-length array of numbers that answers "sum of the first <em>n</em>" and "sum of this range" in
/// O(log n) while staying updatable in O(log n). Also known as a binary indexed tree.
/// </summary>
/// <remarks>
/// <para>
/// Typical uses: mutable histograms and cumulative metrics, leaderboards ("how many scores are below x" when the
/// scores are bucketed), counting inversions, and weighted random selection with changing weights
/// (<see cref="UpperBound"/> on a uniform random number in [0, <see cref="Total"/>)).
/// </para>
/// <para>
/// Arithmetic uses <typeparamref name="T"/>'s own operators, so integer sums wrap on overflow in an unchecked context
/// exactly as a plain loop would, and floating-point sums carry the usual rounding error (an indexer read is the
/// difference of two prefix sums, so it can differ from the stored value in the last bits). Use a wider type such as
/// <see cref="long"/> or <see cref="decimal"/> when sums can grow large. Not thread-safe.
/// </para>
/// </remarks>
/// <typeparam name="T">The number type, such as <see cref="int"/>, <see cref="long"/> or <see cref="double"/>.</typeparam>
[DebuggerDisplay("Length = {Length}")]
public sealed class FenwickTree<T>
    where T : INumber<T>
{
    // 1-based: _tree[i] holds the sum of the values (i - lowbit(i), i]. _tree[0] is unused.
    private readonly T[] _tree;

    /// <summary>Creates a tree of <paramref name="length"/> zeros.</summary>
    /// <param name="length">The number of elements.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    public FenwickTree(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        _tree = new T[length + 1];
        _tree.AsSpan().Fill(T.Zero);
    }

    /// <summary>Creates a tree holding <paramref name="values"/>, in O(n).</summary>
    /// <param name="values">The initial values.</param>
    public FenwickTree(ReadOnlySpan<T> values)
    {
        _tree = new T[values.Length + 1];
        _tree[0] = T.Zero;
        values.CopyTo(_tree.AsSpan(1));
        for (int i = 1; i < _tree.Length; i++)
        {
            int parent = i + (i & -i);
            if (parent < _tree.Length)
            {
                _tree[parent] += _tree[i];
            }
        }
    }

    /// <summary>Gets the number of elements.</summary>
    public int Length => _tree.Length - 1;

    /// <summary>Gets the sum of all elements, in O(log n).</summary>
    public T Total => PrefixSum(Length);

    /// <summary>Gets or sets the element at <paramref name="index"/>, in O(log n).</summary>
    /// <param name="index">The zero-based index.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside [0, Length).</exception>
    public T this[int index]
    {
        get
        {
            CheckIndex(index);
            return PrefixSum(index + 1) - PrefixSum(index);
        }

        set
        {
            CheckIndex(index);
            Add(index, value - this[index]);
        }
    }

    /// <summary>Adds <paramref name="delta"/> to the element at <paramref name="index"/>, in O(log n).</summary>
    /// <param name="index">The zero-based index.</param>
    /// <param name="delta">The amount to add (may be negative).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside [0, Length).</exception>
    public void Add(int index, T delta)
    {
        CheckIndex(index);
        T[] tree = _tree;
        for (int i = index + 1; i < tree.Length; i += i & -i)
        {
            tree[i] += delta;
        }
    }

    /// <summary>Returns the sum of the first <paramref name="count"/> elements, in O(log n).</summary>
    /// <param name="count">How many elements to sum, from index 0.</param>
    /// <returns>The sum; zero when <paramref name="count"/> is 0.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is outside [0, Length].</exception>
    public T PrefixSum(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, Length);
        T[] tree = _tree;
        T sum = T.Zero;
        for (int i = count; i > 0; i &= i - 1)
        {
            sum += tree[i];
        }

        return sum;
    }

    /// <summary>Returns the sum of <paramref name="count"/> elements starting at <paramref name="start"/>, in O(log n).</summary>
    /// <param name="start">The index of the first element.</param>
    /// <param name="count">How many elements to sum.</param>
    /// <returns>The sum.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The range is not inside the tree.</exception>
    public T Sum(int start, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, Length - start);
        return PrefixSum(start + count) - PrefixSum(start);
    }

    /// <summary>
    /// Returns the smallest index <c>i</c> whose prefix sum <c>PrefixSum(i + 1)</c> is at least
    /// <paramref name="target"/>, in O(log n). Requires every element to be non-negative.
    /// </summary>
    /// <param name="target">The cumulative amount to reach.</param>
    /// <returns>The index, or <see cref="Length"/> if even <see cref="Total"/> is below <paramref name="target"/>.</returns>
    public int LowerBound(T target) => Search(target, inclusive: false);

    /// <summary>
    /// Returns the smallest index <c>i</c> whose prefix sum <c>PrefixSum(i + 1)</c> is greater than
    /// <paramref name="target"/>, in O(log n). Requires every element to be non-negative.
    /// </summary>
    /// <remarks>
    /// For weighted random selection with the elements as weights, pass a uniform random number in
    /// [0, <see cref="Total"/>): element <c>i</c> is returned with probability <c>this[i] / Total</c>.
    /// </remarks>
    /// <param name="target">The cumulative amount to exceed.</param>
    /// <returns>The index, or <see cref="Length"/> if <see cref="Total"/> does not exceed <paramref name="target"/>.</returns>
    public int UpperBound(T target) => Search(target, inclusive: true);

    /// <summary>Sets every element to zero.</summary>
    public void Clear() => _tree.AsSpan().Fill(T.Zero);

    /// <summary>Copies the elements to <paramref name="destination"/>, in O(n).</summary>
    /// <param name="destination">Where to copy; at least <see cref="Length"/> long.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too short.</exception>
    public void CopyTo(Span<T> destination)
    {
        if (destination.Length < Length)
        {
            throw new ArgumentException("The destination is too short.", nameof(destination));
        }

        // Undo the construction pass, from the top down.
        destination = destination[..Length];
        _tree.AsSpan(1).CopyTo(destination);
        for (int i = Length; i >= 1; i--)
        {
            int parent = i + (i & -i);
            if (parent <= Length)
            {
                destination[parent - 1] -= destination[i - 1];
            }
        }
    }

    /// <summary>Returns the elements as a new array, in O(n).</summary>
    /// <returns>The elements.</returns>
    public T[] ToArray()
    {
        var values = new T[Length];
        CopyTo(values);
        return values;
    }

    // Binary lifting: descend from the largest power of two, keeping the prefix strictly below (or at most) target.
    private int Search(T target, bool inclusive)
    {
        T[] tree = _tree;
        int length = tree.Length - 1;
        int position = 0;
        T remaining = target;
        for (int step = length == 0 ? 0 : 1 << BitOperations.Log2((uint)length); step > 0; step >>= 1)
        {
            int next = position + step;
            if (next <= length && (inclusive ? tree[next] <= remaining : tree[next] < remaining))
            {
                position = next;
                remaining -= tree[next];
            }
        }

        return position;
    }

    private void CheckIndex(int index) => ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)Length, nameof(index));
}
