using Bojalib.Probabilistic;

namespace Bojalib.Tests.Probabilistic;

/// <summary>The shared header of every probabilistic payload, and pinned bytes that guard each format.</summary>
public class SerializedFormatTests
{
    private static readonly (string Name, Func<byte[]> Write, Action<byte[]> Read)[] Formats =
    [
        ("BloomFilter", static () => Bloom().ToBytes(), static b => BloomFilter<int>.FromBytes(b)),
        ("BinaryFuseFilter", static () => new BinaryFuseFilter<int>([1, 2, 3]).ToBytes(), static b => BinaryFuseFilter<int>.FromBytes(b)),
        ("HyperLogLog", static () => Hll().ToBytes(), static b => HyperLogLog<int>.FromBytes(b)),
        ("CountMinSketch", static () => Cms().ToBytes(), static b => CountMinSketch<int>.FromBytes(b)),
        ("TDigest", static () => Digest().ToBytes(), static b => TDigest.FromBytes(b)),
        ("MinHash", static () => Signature().ToBytes(), static b => MinHash<int>.FromBytes(b)),
    ];

    private static BloomFilter<int> Bloom()
    {
        var filter = new BloomFilter<int>(10, 0.01);
        filter.Add(42);
        return filter;
    }

    private static HyperLogLog<int> Hll()
    {
        var sketch = new HyperLogLog<int>(4);
        sketch.Add(42);
        return sketch;
    }

    private static CountMinSketch<int> Cms()
    {
        var sketch = new CountMinSketch<int>(2, 2);
        sketch.Add(42, 3);
        return sketch;
    }

    private static TDigest Digest()
    {
        var digest = new TDigest();
        digest.Add(1.5);
        return digest;
    }

    private static MinHash<int> Signature()
    {
        var sketch = new MinHash<int>(2);
        sketch.Add(42);
        return sketch;
    }

    [Test]
    public void EveryPayloadStartsWithMagicKindAndVersion()
    {
        for (int kind = 0; kind < Formats.Length; kind++)
        {
            byte[] bytes = Formats[kind].Write();
            bytes.AsSpan(0, 4).ToArray().Should().Equal([(byte)'B', (byte)'J', (byte)(kind + 1), 1], Formats[kind].Name);
            Formats[kind].Read(bytes); // round-trips
        }
    }

    [Test]
    public void AnotherStructuresBytesAreRejectedWithItsName()
    {
        foreach ((string name, Func<byte[]> write, _) in Formats)
        {
            foreach ((string other, _, Action<byte[]> read) in Formats.Where(f => f.Name != name))
            {
                FluentActions.Invoking(() => read(write())).Should().Throw<ArgumentException>()
                    .WithMessage($"Not a serialized {other}: the data holds a {name}.*");
            }
        }
    }

    [Test]
    public void ForeignDataUnknownKindsAndNewerVersionsAreRejected()
    {
        foreach ((string name, Func<byte[]> write, Action<byte[]> read) in Formats)
        {
            byte[] bytes = write();
            FluentActions.Invoking(() => read([])).Should().Throw<ArgumentException>().WithMessage("*doesn't start with a Bojalib header*");
            FluentActions.Invoking(() => read([.. bytes.Skip(4)])).Should().Throw<ArgumentException>("{0} without its header (2.x layout)", name);

            byte[] unknownKind = [.. bytes];
            unknownKind[2] = 99;
            FluentActions.Invoking(() => read(unknownKind)).Should().Throw<ArgumentException>().WithMessage("*unknown kind 99*");

            byte[] newer = [.. bytes];
            newer[3] = 2;
            FluentActions.Invoking(() => read(newer)).Should().Throw<ArgumentException>().WithMessage($"Unsupported {name} format version 2*");
        }
    }

    // Pinned payloads: if one of these changes, saved data from earlier 3.x versions no longer loads correctly.
    // Change a format only together with a new format version (and a reader for the old one).
    [TestCase("BloomFilter", "424A01010000001000100000000000081000000020000000001000000000002000000040")]
    [TestCase("BinaryFuseFilter", "424A02016734D23725B2F66D080000000100000003000000080000000000000000000000000000AF0000000000000000000000000000000000A23300")]
    [TestCase("HyperLogLog", "424A03010400000000000000000000000000020000")]
    [TestCase("CountMinSketch", "424A040102000000020000000003000000000000000300000000000000000000000000000003000000000000000000000000000000")]
    [TestCase("TDigest", "424A050100000000000059400100000000000000000000000000F83F000000000000F83F010000000000000000000000000000000000F83F000000000000F03F")]
    [TestCase("MinHash", "424A0601020000001E5329B1A1DB12970C9CF75CCFFAA3EB")]
    public void PayloadBytesArePinned(string name, string expectedHex)
    {
        string actual = Convert.ToHexString(Formats.Single(f => f.Name == name).Write());
        actual.Should().Be(expectedHex);
    }
}
