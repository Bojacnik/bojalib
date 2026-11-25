namespace Bojalib.Probabilistic;

/// <summary>Which structure a serialized payload holds; stored in its third byte. Never renumber.</summary>
internal enum SerializedKind : byte
{
    BloomFilter = 1,
    BinaryFuseFilter = 2,
    HyperLogLog = 3,
    CountMinSketch = 4,
    TDigest = 5,
    MinHash = 6,
}

/// <summary>
/// The 4-byte prefix of every <c>ToBytes</c> payload in <see cref="Probabilistic"/>: the magic bytes <c>B</c>,
/// <c>J</c>, the <see cref="SerializedKind"/>, and the format version of that kind. It lets <c>FromBytes</c> reject
/// another structure's bytes, foreign data, and formats newer than this library understands.
/// </summary>
internal static class SerializedFormat
{
    public const int PrefixBytes = 4;

    /// <summary>The format version written today. Bump it for a kind when its layout or hashing changes.</summary>
    private const byte CurrentVersion = 1;

    private const byte Magic0 = (byte)'B';
    private const byte Magic1 = (byte)'J';

    /// <summary>Allocates a payload for <paramref name="kind"/>, writes the prefix and returns the body to fill.</summary>
    public static byte[] Create(SerializedKind kind, int bodyLength, out Span<byte> body)
    {
        byte[] bytes = new byte[PrefixBytes + bodyLength];
        bytes[0] = Magic0;
        bytes[1] = Magic1;
        bytes[2] = (byte)kind;
        bytes[3] = CurrentVersion;
        body = bytes.AsSpan(PrefixBytes);
        return bytes;
    }

    /// <summary>Checks the prefix and returns the body.</summary>
    /// <exception cref="ArgumentException">The bytes are not a payload of <paramref name="kind"/> in a known version.</exception>
    public static ReadOnlySpan<byte> Body(ReadOnlySpan<byte> bytes, SerializedKind kind, string paramName)
    {
        if (bytes.Length < PrefixBytes || bytes[0] != Magic0 || bytes[1] != Magic1)
        {
            throw new ArgumentException($"Not a serialized {kind}: the data doesn't start with a Bojalib header.", paramName);
        }

        if (bytes[2] != (byte)kind)
        {
            string actual = Enum.IsDefined((SerializedKind)bytes[2]) ? ((SerializedKind)bytes[2]).ToString() : $"unknown kind {bytes[2]}";
            throw new ArgumentException($"Not a serialized {kind}: the data holds a {actual}.", paramName);
        }

        if (bytes[3] != CurrentVersion)
        {
            throw new ArgumentException($"Unsupported {kind} format version {bytes[3]}; this library reads version {CurrentVersion}.", paramName);
        }

        return bytes[PrefixBytes..];
    }
}
