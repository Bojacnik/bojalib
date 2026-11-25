using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Bojalib.Hashing;

namespace Bojalib.Probabilistic;

/// <summary>
/// A fixed-size signature of a set that estimates how similar two sets are — their Jaccard similarity
/// |A ∩ B| / |A ∪ B| — without keeping the sets (Broder's MinHash, 1997).
/// </summary>
/// <remarks>
/// <para>
/// Use it for near-duplicate detection (documents as sets of shingles or words), clustering, recommendation ("users
/// with similar item sets") and estimating set overlap across machines. Each sketch keeps the minimum of
/// <see cref="SignatureSize"/> independent hash functions over the items it has seen. Two sets agree on a given
/// minimum with probability exactly equal to their Jaccard similarity, so the fraction of agreeing minima estimates
/// it without bias, with standard error sqrt(J(1 − J) / k) — at most 0.5 / sqrt(k), so ±4.4% for the default
/// k = 128 and ±2.2% for k = 512.
/// </para>
/// <para>
/// Adding an item costs k hash evaluations; adding the same item again changes nothing. Sketches with the same
/// signature size and hashing can be merged (<see cref="UnionWith"/>) into the sketch of the union. Strings and
/// primitives hash stably, so <see cref="ToBytes"/>/<see cref="FromBytes"/> work across processes and machines;
/// other types need a custom hasher for that (see <see cref="IsPortable"/>). <see cref="Signature"/> exposes the raw
/// minima for locality-sensitive hashing. Not thread-safe.
/// </para>
/// </remarks>
/// <typeparam name="T">The item type.</typeparam>
[DebuggerDisplay("SignatureSize = {SignatureSize}, IsEmpty = {IsEmpty}")]
public sealed class MinHash<T>
{
    private const int HeaderBytes = 4;

    private readonly ulong[] _minima;
    private readonly ulong[] _seeds;
    private readonly Func<T, ulong>? _hasher;

    /// <summary>Creates an empty sketch.</summary>
    /// <param name="signatureSize">
    /// The number of hash functions k, 1–65536. The error shrinks with 1/sqrt(k); memory is 8k bytes.
    /// </param>
    /// <param name="hasher">An optional custom 64-bit hash.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="signatureSize"/> is outside 1–65536.</exception>
    public MinHash(int signatureSize = 128, Func<T, ulong>? hasher = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(signatureSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(signatureSize, 65536);
        _minima = new ulong[signatureSize];
        _minima.AsSpan().Fill(ulong.MaxValue);
        _seeds = signatureSize <= MinHashSeeds.Shared.Length ? MinHashSeeds.Shared : MinHashSeeds.Create(signatureSize);
        _hasher = hasher;
    }

    /// <summary>Gets the number of hash functions (minima) in the signature.</summary>
    public int SignatureSize => _minima.Length;

    /// <summary>Gets the worst-case standard error of <see cref="Similarity"/>: 0.5 / sqrt(<see cref="SignatureSize"/>).</summary>
    public double StandardError => 0.5 / Math.Sqrt(_minima.Length);

    /// <summary>Gets whether no item has been added.</summary>
    public bool IsEmpty => _minima.AsSpan().IndexOfAnyExcept(ulong.MaxValue) < 0;

    /// <summary>Gets the raw signature: the minimum of each hash function (<see cref="ulong.MaxValue"/> while empty).</summary>
    public ReadOnlySpan<ulong> Signature => _minima;

    /// <summary>Gets whether <see cref="ToBytes"/> output is meaningful in another process (see <see cref="BloomFilter{T}.IsPortable"/>).</summary>
    public bool IsPortable => ItemHasher<T>.IsPortable(_hasher);

    /// <summary>Adds <paramref name="item"/> to the set the sketch describes.</summary>
    /// <param name="item">The item.</param>
    public void Add(T item)
    {
        ulong hash = ItemHasher<T>.Hash(item, _hasher);
        Span<ulong> minima = _minima;
        ReadOnlySpan<ulong> seeds = _seeds.AsSpan(0, minima.Length);
        for (int i = 0; i < minima.Length; i++)
        {
            ulong h = ItemHasher<T>.Mix(hash ^ seeds[i]);
            if (h < minima[i])
            {
                minima[i] = h;
            }
        }
    }

    /// <summary>
    /// Estimates the Jaccard similarity of the two sets: 1 for identical sets, 0 for disjoint ones. Two empty
    /// sketches count as identical (1); an empty and a non-empty one as disjoint (0).
    /// </summary>
    /// <param name="other">A sketch with the same signature size and hashing.</param>
    /// <returns>The estimate, between 0 and 1.</returns>
    /// <exception cref="ArgumentException">The signature sizes differ.</exception>
    public double Similarity(MinHash<T> other)
    {
        CheckCompatible(other);
        ReadOnlySpan<ulong> a = _minima;
        ReadOnlySpan<ulong> b = other._minima;
        int equal = 0;
        int i = 0;
        if (Vector256.IsHardwareAccelerated)
        {
            ref ulong x = ref MemoryMarshal.GetReference(a);
            ref ulong y = ref MemoryMarshal.GetReference(b);
            for (; i + Vector256<ulong>.Count <= a.Length; i += Vector256<ulong>.Count)
            {
                Vector256<ulong> same = Vector256.Equals(Vector256.LoadUnsafe(ref x, (nuint)i), Vector256.LoadUnsafe(ref y, (nuint)i));
                equal += System.Numerics.BitOperations.PopCount(same.ExtractMostSignificantBits());
            }
        }

        for (; i < a.Length; i++)
        {
            if (a[i] == b[i])
            {
                equal++;
            }
        }

        return equal / (double)a.Length;
    }

    /// <summary>Merges <paramref name="other"/> into this sketch, so it describes the union of both sets.</summary>
    /// <param name="other">A sketch with the same signature size and hashing.</param>
    /// <exception cref="ArgumentException">The signature sizes differ.</exception>
    public void UnionWith(MinHash<T> other)
    {
        CheckCompatible(other);
        Span<ulong> a = _minima;
        ReadOnlySpan<ulong> b = other._minima;
        for (int i = 0; i < a.Length; i++)
        {
            a[i] = Math.Min(a[i], b[i]);
        }
    }

    /// <summary>Resets the sketch to empty.</summary>
    public void Clear() => _minima.AsSpan().Fill(ulong.MaxValue);

    /// <summary>Serializes the sketch (little-endian): the 4-byte Bojalib header, the signature size, then the minima.</summary>
    /// <returns>The sketch's bytes.</returns>
    public byte[] ToBytes()
    {
        byte[] bytes = SerializedFormat.Create(SerializedKind.MinHash, HeaderBytes + (_minima.Length * 8), out Span<byte> body);
        BinaryPrimitives.WriteInt32LittleEndian(body, _minima.Length);
        for (int i = 0; i < _minima.Length; i++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(body[(HeaderBytes + (i * 8))..], _minima[i]);
        }

        return bytes;
    }

    /// <summary>Recreates a sketch from <see cref="ToBytes"/> output.</summary>
    /// <param name="bytes">The serialized sketch.</param>
    /// <param name="hasher">The same custom hasher the sketch was built with, if any.</param>
    /// <returns>The sketch.</returns>
    /// <exception cref="ArgumentException"><paramref name="bytes"/> is not a serialized sketch.</exception>
#pragma warning disable CA1000 // Factory on the generic type it creates.
    public static MinHash<T> FromBytes(ReadOnlySpan<byte> bytes, Func<T, ulong>? hasher = null)
#pragma warning restore CA1000
    {
        bytes = SerializedFormat.Body(bytes, SerializedKind.MinHash, nameof(bytes));
        if (bytes.Length < HeaderBytes)
        {
            throw new ArgumentException("Not a serialized MinHash sketch.", nameof(bytes));
        }

        int size = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        if (size is < 1 or > 65536 || bytes.Length != HeaderBytes + (size * 8))
        {
            throw new ArgumentException("Not a serialized MinHash sketch.", nameof(bytes));
        }

        var sketch = new MinHash<T>(size, hasher);
        for (int i = 0; i < size; i++)
        {
            sketch._minima[i] = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(HeaderBytes + (i * 8))..]);
        }

        return sketch;
    }

    private void CheckCompatible(MinHash<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other._minima.Length != _minima.Length)
        {
            throw new ArgumentException("Sketches must have the same signature size.", nameof(other));
        }
    }
}

// Per-function seeds, the same in every process and for every item type: the SplitMix64 sequence from a fixed start.
internal static class MinHashSeeds
{
    public static readonly ulong[] Shared = Create(1024);

    public static ulong[] Create(int count)
    {
        var seeds = new ulong[count];
        ulong state = 0x6A09E667F3BCC908UL;
        for (int i = 0; i < count; i++)
        {
            state += 0x9E3779B97F4A7C15UL;
            seeds[i] = ItemHasher<ulong>.Mix(state);
        }

        return seeds;
    }
}
