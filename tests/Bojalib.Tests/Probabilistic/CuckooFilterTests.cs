using Bojalib.Probabilistic;
using CsCheck;

namespace Bojalib.Tests.Probabilistic;

public class CuckooFilterTests
{
    [Test]
    public void RandomAddsAndRemoves_NeverGiveFalseNegatives([Values(8, 16)] int bits, [Values(2, 4, 8)] int bucketSize)
    {
        Gen.Select(Gen.Int[0, 300], Gen.Bool).Array[0, 600].Sample(ops =>
        {
            var filter = new CuckooFilter<int>(400, bits, bucketSize);
            var model = new Dictionary<int, int>(); // item -> copies stored
            foreach ((int item, bool add) in ops)
            {
                if (add)
                {
                    if (filter.TryAdd(item))
                    {
                        model[item] = model.GetValueOrDefault(item) + 1;
                    }
                }
                else if (model.GetValueOrDefault(item) > 0)
                {
                    // Only items that were added may be removed.
                    filter.Remove(item).Should().BeTrue();
                    model[item]--;
                }
            }

            foreach ((int item, int copies) in model)
            {
                if (copies > 0)
                {
                    filter.MightContain(item).Should().BeTrue("item {0} was added and not removed", item);
                }
            }

            filter.Count.Should().Be(model.Values.Sum());
        }, iter: TestScale.Iterations(300));
    }

    [Test]
    public void FillsToTheDocumentedLoad_AndAFailedAddChangesNothing([Values(2, 4, 8)] int bucketSize)
    {
        var filter = new CuckooFilter<long>(10_000, 16, bucketSize);
        long added = 0;
        while (filter.TryAdd(added))
        {
            added++;
        }

        double expectedLoad = bucketSize switch { 2 => 0.84, 4 => 0.95, _ => 0.98 };
        filter.LoadFactor.Should().BeGreaterThan(expectedLoad - 0.05, "{0}-slot buckets fill to about {1:P0}", bucketSize, expectedLoad);
        filter.Count.Should().Be((int)added);
        for (long i = 0; i < added; i++)
        {
            filter.MightContain(i).Should().BeTrue("a failed insertion must not evict anything");
        }

    }

    [Test]
    public void DuplicatesAreStoredUpToTwoBuckets()
    {
        var filter = new CuckooFilter<string>(1000, bucketSize: 4);
        int copies = 0;
        while (filter.TryAdd("same"))
        {
            copies++;
        }

        copies.Should().BeInRange(4, 8, "one item has two buckets of four slots (fewer if both hash to the same bucket)");
        filter.Invoking(f => f.Add("same")).Should().Throw<InvalidOperationException>();
        filter.Count.Should().Be(copies, "the failed add changed nothing");
        for (int i = 0; i < copies; i++)
        {
            filter.MightContain("same").Should().BeTrue();
            filter.Remove("same").Should().BeTrue();
        }

        filter.MightContain("same").Should().BeFalse();
        filter.Remove("same").Should().BeFalse();
    }

    [Test]
    public void PropertiesAndValidation()
    {
        var filter = new CuckooFilter<int>(100, fingerprintBits: 8);
        filter.FingerprintBits.Should().Be(8);
        filter.BucketSize.Should().Be(4);
        filter.Capacity.Should().BeGreaterThanOrEqualTo(100);
        filter.ByteCount.Should().Be(filter.Capacity);
        filter.FalsePositiveRate.Should().BeApproximately(8.0 / 255, 1e-12);
        new CuckooFilter<int>(100).ByteCount.Should().Be(new CuckooFilter<int>(100).Capacity * 2);
        new CuckooFilter<int>(100).FalsePositiveRate.Should().BeLessThan(0.0002);
        new CuckooFilter<int>(0).Capacity.Should().BeGreaterThan(0);

        filter.Add(5);
        filter.Clear();
        filter.Count.Should().Be(0);
        filter.MightContain(5).Should().BeFalse();
        var wide = new CuckooFilter<int>(100);
        wide.Add(5);
        wide.Clear();
        wide.MightContain(5).Should().BeFalse();

        var custom = new CuckooFilter<int>(100, hasher: static i => (ulong)i * 0x9E3779B97F4A7C15UL);
        custom.Add(3);
        custom.MightContain(3).Should().BeTrue();

        FluentActions.Invoking(() => new CuckooFilter<int>(-1)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new CuckooFilter<int>(10, 12)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new CuckooFilter<int>(10, 8, 3)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new CuckooFilter<int>(int.MaxValue)).Should().Throw<ArgumentOutOfRangeException>();
    }
}
