using Bojalib.Probabilistic;
using CsCheck;

namespace Bojalib.Tests.Probabilistic;

public class MinHashTests
{
    private static MinHash<int> Sketch(IEnumerable<int> items, int size = 256)
    {
        var sketch = new MinHash<int>(size);
        foreach (int item in items)
        {
            sketch.Add(item);
        }

        return sketch;
    }

    [TestCase(0.0)]
    [TestCase(0.1)]
    [TestCase(0.5)]
    [TestCase(0.9)]
    [TestCase(1.0)]
    public void Similarity_EstimatesJaccardWithinTheStandardError(double jaccard)
    {
        // |A| = |B| = n with shared s: J = s / (2n - s), so s = 2nJ / (1 + J).
        const int n = 5000;
        int shared = (int)Math.Round(2 * n * jaccard / (1 + jaccard));
        double actual = shared / (double)((2 * n) - shared);
        var errors = new List<double>();
        for (int run = 0; run < 20; run++)
        {
            int offset = run * 1_000_000;
            MinHash<int> a = Sketch(Enumerable.Range(offset, n));
            MinHash<int> b = Sketch(Enumerable.Range(offset + n - shared, n));
            errors.Add(a.Similarity(b) - actual);
        }

        double bound = Math.Sqrt(actual * (1 - actual) / 256);
        Math.Abs(errors.Average()).Should().BeLessThan((4 * bound / Math.Sqrt(20)) + 1e-9, "the estimator is unbiased");
        errors.Max(Math.Abs).Should().BeLessThanOrEqualTo((5 * bound) + 1e-9);
    }

    [Test]
    public void Merging_GivesTheSketchOfTheUnion()
    {
        Gen.Select(Gen.Int[0, 500].HashSet[0, 100], Gen.Int[0, 500].HashSet[0, 100]).Sample(t =>
        {
            (HashSet<int> a, HashSet<int> b) = t;
            MinHash<int> merged = Sketch(a, 64);
            merged.UnionWith(Sketch(b, 64));
            merged.Signature.SequenceEqual(Sketch(a.Union(b), 64).Signature).Should().BeTrue();
        }, iter: TestScale.Iterations(500));
    }

    [Test]
    public void EmptySketchesSerializationAndValidation()
    {
        var empty = new MinHash<string>();
        empty.IsEmpty.Should().BeTrue();
        empty.SignatureSize.Should().Be(128);
        empty.StandardError.Should().BeApproximately(0.5 / Math.Sqrt(128), 1e-12);
        empty.IsPortable.Should().BeTrue();
        empty.Similarity(new MinHash<string>()).Should().Be(1, "two empty sets count as identical");

        var words = new MinHash<string>();
        foreach (string w in "the quick brown fox".Split(' '))
        {
            words.Add(w);
        }

        words.IsEmpty.Should().BeFalse();
        words.Similarity(empty).Should().Be(0, "a non-empty and an empty set are disjoint");
        words.Similarity(words).Should().Be(1);

        MinHash<string> copy = MinHash<string>.FromBytes(words.ToBytes());
        copy.Similarity(words).Should().Be(1);
        copy.Signature.SequenceEqual(words.Signature).Should().BeTrue();

        words.Clear();
        words.IsEmpty.Should().BeTrue();

        new MinHash<object>().IsPortable.Should().BeFalse();
        new MinHash<object>(hasher: static o => 1).IsPortable.Should().BeTrue();
        new MinHash<int>(5000).SignatureSize.Should().Be(5000, "sizes beyond the shared seed table get their own seeds");
        Sketch([1, 2, 3], 7).Similarity(Sketch([1, 2, 3], 7)).Should().Be(1, "the scalar tail of the comparison works too");

        FluentActions.Invoking(() => new MinHash<int>(0)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new MinHash<int>(65537)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new MinHash<int>(8).Similarity(new MinHash<int>(16))).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new MinHash<int>(8).UnionWith(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => MinHash<int>.FromBytes([1, 2])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => MinHash<int>.FromBytes([0, 0, 0, 0])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => MinHash<int>.FromBytes([1, 0, 0, 0, 1])).Should().Throw<ArgumentException>();
    }

    [Test]
    public void HashingIsStableAcrossProcesses()
    {
        // Pinned values: a change here breaks sketches saved by earlier versions.
        var sketch = new MinHash<string>(4);
        sketch.Add("bojalib");
        sketch.Add("bojalib");
        Convert.ToHexString(sketch.ToBytes()).Should().Be("424A0601040000008B5E7B2689757389404E2BE1875AF2A7DEA379CDD34D12E63039E20FF1BB2671");
    }
}
