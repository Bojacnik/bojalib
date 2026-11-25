using Bojalib.Collections;
using CsCheck;

namespace Bojalib.Tests.Collections;

/// <summary>In-place operations, counts and ranges of <see cref="RoaringBitmap"/>, against <see cref="SortedSet{T}"/>.</summary>
public class RoaringSetOperationTests
{
    // Values clustered in a few chunks, with sizes on both sides of the 4096 array/bitmap threshold.
    private static readonly Gen<uint[]> Values = Gen.OneOf(
        Gen.UInt[0, 200_000].Array[0, 300],
        Gen.Select(Gen.UInt[0, 3], Gen.Int[0, 9000]).SelectMany(t => Gen.UInt[0, 65535].Array[t.Item2].Select(a => a.Select(v => (t.Item1 << 16) | v).ToArray())));

    [Test]
    public void InPlaceOperationsAndCounts_MatchSortedSet()
    {
        Gen.Select(Values, Values).Sample(t =>
        {
            (uint[] left, uint[] right) = t;
            var a = new SortedSet<uint>(left);
            var b = new SortedSet<uint>(right);
            var rb = new RoaringBitmap(right);

            void Same(Action<RoaringBitmap> roaring, Action<SortedSet<uint>> reference)
            {
                var bitmap = new RoaringBitmap(left);
                var expected = new SortedSet<uint>(left);
                roaring(bitmap);
                reference(expected);
                bitmap.ToArray().Should().Equal(expected);
                RoaringBitmap.FromBytes(bitmap.ToBytes()).Equals(bitmap).Should().BeTrue();
            }

            Same(x => x.UnionWith(rb), x => x.UnionWith(b));
            Same(x => x.IntersectWith(rb), x => x.IntersectWith(b));
            Same(x => x.ExceptWith(rb), x => x.ExceptWith(b));
            Same(x => x.SymmetricExceptWith(rb), x => x.SymmetricExceptWith(b));

            var ra = new RoaringBitmap(left);
            long intersection = a.Count(b.Contains);
            ra.IntersectionCount(rb).Should().Be(intersection);
            ra.UnionCount(rb).Should().Be(a.Count + b.Count - intersection);
            ra.Overlaps(rb).Should().Be(intersection > 0);
            ra.IsSubsetOf(rb).Should().Be(a.IsSubsetOf(b));
            rb.ToArray().Should().Equal(b, "the argument of an in-place operation is left alone");
        }, iter: TestScale.Iterations(150));
    }

    [Test]
    public void RemoveRange_MatchesSortedSet()
    {
        Gen.Select(Values, Gen.UInt[0, 210_000], Gen.UInt[0, 140_000]).Sample(t =>
        {
            (uint[] values, uint start, uint length) = t;
            uint end = start + length;
            var bitmap = new RoaringBitmap(values);
            bitmap.RemoveRange(start, end);
            bitmap.ToArray().Should().Equal(values.Where(v => v < start || v > end).Distinct().Order());
        }, iter: TestScale.Iterations(250));
    }

    [Test]
    public void RemoveRange_EdgesOfTheValueSpace()
    {
        var bitmap = new RoaringBitmap([0, 1, uint.MaxValue - 1, uint.MaxValue]);
        bitmap.RemoveRange(uint.MaxValue, uint.MaxValue);
        bitmap.RemoveRange(0, 0);
        bitmap.ToArray().Should().Equal(1u, uint.MaxValue - 1);
        bitmap.RemoveRange(0, uint.MaxValue);
        bitmap.IsEmpty.Should().BeTrue();
        bitmap.Invoking(b => b.RemoveRange(5, 4)).Should().Throw<ArgumentException>();

        bitmap.AddRange(0, 200_000);
        bitmap.RemoveRange(10, 199_990);
        bitmap.Count.Should().Be(20, "0–9 and 199,991–200,000 remain");
    }

    [Test]
    public void UnionOfMany_MatchesPairwiseUnion()
    {
        Values.Array[0, 6].Sample(sets =>
        {
            RoaringBitmap[] bitmaps = [.. sets.Select(static s => new RoaringBitmap(s))];
            RoaringBitmap union = RoaringBitmap.Union(bitmaps);
            union.ToArray().Should().Equal(sets.SelectMany(static s => s).Distinct().Order());
            union.Add(7);
            bitmaps.Select(static b => b.Count).Should().Equal(sets.Select(static s => (long)s.Distinct().Count()), "inputs are not shared with the result");
        }, iter: TestScale.Iterations(100));
    }

    [Test]
    public void SelfOperationsAndNullArguments()
    {
        var bitmap = new RoaringBitmap([1, 2, 100_000]);
        bitmap.UnionWith(bitmap);
        bitmap.IntersectWith(bitmap);
        bitmap.Count.Should().Be(3);
        bitmap.IsSubsetOf(bitmap).Should().BeTrue();
        bitmap.Overlaps(bitmap).Should().BeTrue();
        bitmap.IntersectionCount(bitmap).Should().Be(3);
        bitmap.SymmetricExceptWith(bitmap);
        bitmap.IsEmpty.Should().BeTrue();
        bitmap.Add(5);
        bitmap.ExceptWith(bitmap);
        bitmap.IsEmpty.Should().BeTrue();
        bitmap.Overlaps(new RoaringBitmap([1])).Should().BeFalse();
        new RoaringBitmap([1, 70_000]).IsSubsetOf(new RoaringBitmap([1])).Should().BeFalse();

        foreach (Action<RoaringBitmap> call in new Action<RoaringBitmap>[]
        {
            static b => b.UnionWith(null!), static b => b.IntersectWith(null!), static b => b.ExceptWith(null!),
            static b => b.SymmetricExceptWith(null!), static b => b.IntersectionCount(null!), static b => b.UnionCount(null!),
            static b => b.Overlaps(null!), static b => b.IsSubsetOf(null!), static b => RoaringBitmap.Union(b, null!),
        })
        {
            bitmap.Invoking(call).Should().Throw<ArgumentNullException>();
        }
    }
}
