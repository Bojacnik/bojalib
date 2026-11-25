using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Bojalib.Hashing;

namespace Bojalib.Probabilistic;

/// <summary>
/// A set-membership filter that, unlike a Bloom filter, supports <see cref="Remove"/> (Fan, Andersen, Kaminsky &amp;
/// Mitzenmacher's cuckoo filter, 2014). Like other filters it never gives false negatives for items that were added
/// and not removed, and occasionally says "maybe" for items that were never added.
/// </summary>
/// <remarks>
/// <para>
/// Each item is stored as a short fingerprint in one of two buckets. A lookup reads two buckets, so it touches at
/// most two cache lines. With the default 4 slots per bucket and 16-bit fingerprints, the false-positive rate is at
/// most about 0.012% at full load and the filter costs about 17 bits per item; 8-bit fingerprints give about 3% at
/// half the size. The filter has a fixed capacity: it fills to roughly 95% of its slots (84% with 2-slot buckets,
/// 98% with 8-slot ones) before <see cref="TryAdd"/> starts to fail. A failed add leaves the filter unchanged.
/// </para>
/// <para>
/// Pick the filter by workload: <see cref="BloomFilter{T}"/> for a growing set that never shrinks,
/// <see cref="BinaryFuseFilter{T}"/> for a fixed set (the smallest), and this one when items must also be removed.
/// </para>
/// <para>
/// Only remove items that were added: removing an item that was never added may delete the fingerprint of a
/// different item that happens to collide, which would then give a false negative. Adding the same item more than
/// once stores it more than once (so it survives one removal per add), and at most 2 × <see cref="BucketSize"/>
/// copies of one item fit. Not thread-safe.
/// </para>
/// </remarks>
/// <typeparam name="T">The item type.</typeparam>
[DebuggerDisplay("Count = {Count}, Capacity = {Capacity}")]
public sealed class CuckooFilter<T>
{
    private const int MaxKicks = 500;

    private readonly Func<T, ulong>? _hasher;
    private readonly byte[]? _fingerprints8;
    private readonly ushort[]? _fingerprints16;
    private readonly int _bucketMask;
    private readonly uint _fingerprintMask;
    private ulong _random = 0x9E3779B97F4A7C15UL;

    /// <summary>Creates an empty filter with room for at least <paramref name="capacity"/> items.</summary>
    /// <param name="capacity">How many items the filter must hold. The table size is rounded up to a power of two buckets.</param>
    /// <param name="fingerprintBits">8 (≈3% false positives) or 16 (≈0.012%, twice the size).</param>
    /// <param name="bucketSize">Slots per bucket: 2, 4 or 8. Larger buckets fill further but give more false positives.</param>
    /// <param name="hasher">An optional custom 64-bit hash.</param>
    /// <exception cref="ArgumentOutOfRangeException">An argument is out of range.</exception>
    public CuckooFilter(int capacity, int fingerprintBits = 16, int bucketSize = 4, Func<T, ulong>? hasher = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        if (fingerprintBits is not (8 or 16))
        {
            throw new ArgumentOutOfRangeException(nameof(fingerprintBits), "Must be 8 or 16.");
        }

        if (bucketSize is not (2 or 4 or 8))
        {
            throw new ArgumentOutOfRangeException(nameof(bucketSize), "Must be 2, 4 or 8.");
        }

        double maxLoad = bucketSize switch { 2 => 0.84, 4 => 0.95, _ => 0.98 };
        long buckets = Math.Max(2, (long)Math.Ceiling(capacity / (bucketSize * maxLoad)));
        if (buckets > 1 << 28)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "The filter would be too large.");
        }

        int bucketCount = (int)BitOperations.RoundUpToPowerOf2((uint)buckets);
        _bucketMask = bucketCount - 1;
        _fingerprintMask = fingerprintBits == 8 ? 0xFFu : 0xFFFFu;
        BucketSize = bucketSize;
        _hasher = hasher;
        if (fingerprintBits == 8)
        {
            _fingerprints8 = new byte[bucketCount * bucketSize];
        }
        else
        {
            _fingerprints16 = new ushort[bucketCount * bucketSize];
        }
    }

    /// <summary>Gets the number of stored items (each add counts, duplicates included).</summary>
    public int Count { get; private set; }

    /// <summary>Gets the number of slots. The filter can't fill all of them; see the remarks on <see cref="CuckooFilter{T}"/>.</summary>
    public int Capacity => (_bucketMask + 1) * BucketSize;

    /// <summary>Gets the fraction of slots in use.</summary>
    public double LoadFactor => Count / (double)Capacity;

    /// <summary>Gets the number of slots per bucket.</summary>
    public int BucketSize { get; }

    /// <summary>Gets the fingerprint size: 8 or 16 bits.</summary>
    public int FingerprintBits => _fingerprints8 is not null ? 8 : 16;

    /// <summary>
    /// Gets the false-positive rate at full load, an upper bound: a lookup compares against up to
    /// 2 × <see cref="BucketSize"/> fingerprints, each a false match with probability 1 / (2^<see cref="FingerprintBits"/> − 1).
    /// </summary>
    public double FalsePositiveRate => 2.0 * BucketSize / ((1 << FingerprintBits) - 1);

    /// <summary>Gets the size of the fingerprint table in bytes.</summary>
    public int ByteCount => _fingerprints8?.Length ?? (_fingerprints16!.Length * 2);

    /// <summary>Adds <paramref name="item"/>.</summary>
    /// <param name="item">The item.</param>
    /// <exception cref="InvalidOperationException">The filter is full; it is left unchanged.</exception>
    public void Add(T item)
    {
        if (!TryAdd(item))
        {
            throw new InvalidOperationException("The cuckoo filter is full.");
        }
    }

    /// <summary>Adds <paramref name="item"/> if there is room.</summary>
    /// <param name="item">The item.</param>
    /// <returns><see langword="true"/> if it was added; <see langword="false"/> if the filter is full, in which case it is unchanged.</returns>
    public bool TryAdd(T item)
    {
        (uint fingerprint, int bucket) = Locate(item);
        bool added = _fingerprints8 is not null
            ? Insert(_fingerprints8, (byte)fingerprint, bucket)
            : Insert(_fingerprints16!, (ushort)fingerprint, bucket);
        if (added)
        {
            Count++;
        }

        return added;
    }

    /// <summary>Returns <see langword="false"/> if <paramref name="item"/> is definitely not in the filter, <see langword="true"/> if it probably is.</summary>
    /// <param name="item">The item.</param>
    /// <returns>Whether the item might be in the filter.</returns>
    public bool MightContain(T item)
    {
        (uint fingerprint, int bucket) = Locate(item);
        int other = Alternate(bucket, fingerprint);
        return _fingerprints8 is not null
            ? IndexOf(_fingerprints8, bucket, (byte)fingerprint) >= 0 || IndexOf(_fingerprints8, other, (byte)fingerprint) >= 0
            : IndexOf(_fingerprints16!, bucket, (ushort)fingerprint) >= 0 || IndexOf(_fingerprints16!, other, (ushort)fingerprint) >= 0;
    }

    /// <summary>Removes one copy of <paramref name="item"/>. Only remove items that were added (see the remarks).</summary>
    /// <param name="item">The item.</param>
    /// <returns><see langword="true"/> if a matching fingerprint was found and removed.</returns>
    public bool Remove(T item)
    {
        (uint fingerprint, int bucket) = Locate(item);
        bool removed = _fingerprints8 is not null
            ? Delete(_fingerprints8, (byte)fingerprint, bucket)
            : Delete(_fingerprints16!, (ushort)fingerprint, bucket);
        if (removed)
        {
            Count--;
        }

        return removed;
    }

    /// <summary>Removes all items.</summary>
    public void Clear()
    {
        if (_fingerprints8 is not null)
        {
            Array.Clear(_fingerprints8);
        }
        else
        {
            Array.Clear(_fingerprints16!);
        }

        Count = 0;
    }

    // Fingerprint from the high half of the hash (0 is reserved for "empty"), bucket from the low half.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (uint Fingerprint, int Bucket) Locate(T item)
    {
        ulong hash = ItemHasher<T>.Hash(item, _hasher);
        uint fingerprint = (uint)(hash >> 32) & _fingerprintMask;
        return (fingerprint == 0 ? 1 : fingerprint, (int)hash & _bucketMask);
    }

    // Partial-key cuckoo hashing: the other bucket depends only on this bucket and the fingerprint, and the
    // XOR makes it symmetric, so an item can be moved between its two buckets without knowing the item.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Alternate(int bucket, uint fingerprint) => (bucket ^ (int)ItemHasher<T>.Mix(fingerprint)) & _bucketMask;

    private int IndexOf<TFingerprint>(TFingerprint[] table, int bucket, TFingerprint fingerprint)
        where TFingerprint : unmanaged, IBinaryInteger<TFingerprint>
    {
        int start = bucket * BucketSize;
        for (int slot = start; slot < start + BucketSize; slot++)
        {
            if (table[slot] == fingerprint)
            {
                return slot;
            }
        }

        return -1;
    }

    private bool Insert<TFingerprint>(TFingerprint[] table, TFingerprint fingerprint, int bucket)
        where TFingerprint : unmanaged, IBinaryInteger<TFingerprint>
    {
        int other = Alternate(bucket, uint.CreateTruncating(fingerprint));
        int slot = IndexOf(table, bucket, TFingerprint.Zero);
        if (slot < 0)
        {
            slot = IndexOf(table, other, TFingerprint.Zero);
        }

        if (slot >= 0)
        {
            table[slot] = fingerprint;
            return true;
        }

        // Both buckets full: evict a random fingerprint to its own alternate bucket, and repeat. Every swap is
        // recorded so a failed insertion can be undone and no stored fingerprint is ever lost.
        Span<int> swapped = stackalloc int[MaxKicks];
        bucket = (NextRandom() & 1) == 0 ? bucket : other;
        TFingerprint carried = fingerprint;
        for (int kick = 0; kick < MaxKicks; kick++)
        {
            slot = (bucket * BucketSize) + (int)(NextRandom() % (uint)BucketSize);
            swapped[kick] = slot;
            (carried, table[slot]) = (table[slot], carried);
            bucket = Alternate(bucket, uint.CreateTruncating(carried));
            int free = IndexOf(table, bucket, TFingerprint.Zero);
            if (free >= 0)
            {
                table[free] = carried;
                return true;
            }
        }

        for (int kick = MaxKicks - 1; kick >= 0; kick--)
        {
            (carried, table[swapped[kick]]) = (table[swapped[kick]], carried);
        }

        Debug.Assert(carried == fingerprint, "Undoing the kicks must give back the new fingerprint.");
        return false;
    }

    private bool Delete<TFingerprint>(TFingerprint[] table, TFingerprint fingerprint, int bucket)
        where TFingerprint : unmanaged, IBinaryInteger<TFingerprint>
    {
        int slot = IndexOf(table, bucket, fingerprint);
        if (slot < 0)
        {
            slot = IndexOf(table, Alternate(bucket, uint.CreateTruncating(fingerprint)), fingerprint);
        }

        if (slot < 0)
        {
            return false;
        }

        table[slot] = TFingerprint.Zero;
        return true;
    }

    // xorshift64: only picks which fingerprint to evict, so quality needs are modest; deterministic for reproducibility.
    private uint NextRandom()
    {
        ulong x = _random;
        x ^= x << 13;
        x ^= x >> 7;
        x ^= x << 17;
        _random = x;
        return (uint)(x >> 32);
    }
}
