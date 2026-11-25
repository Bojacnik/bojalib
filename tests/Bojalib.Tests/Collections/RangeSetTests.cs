using Bojalib.Collections;
using CsCheck;

namespace Bojalib.Tests.Collections;

public class RangeSetTests
{
    private const int Universe = 64;

    // The model: one bool per point in [0, Universe).
    private static List<HalfOpenRange<int>> RangesOf(bool[] points)
    {
        var ranges = new List<HalfOpenRange<int>>();
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] && (i == 0 || !points[i - 1]))
            {
                int end = i;
                while (end < points.Length && points[end])
                {
                    end++;
                }

                ranges.Add(new(i, end));
            }
        }

        return ranges;
    }

    private static void ShouldMatch(RangeSet<int> set, bool[] model)
    {
        set.ToList().Should().Equal(RangesOf(model));
        set.Count.Should().Be(RangesOf(model).Count);
    }

    private static readonly Gen<(int Op, int Start, int Length)> Operation = Gen.Select(Gen.Int[0, 6], Gen.Int[0, Universe - 1], Gen.Int[0, 12]);

    [Test]
    public void RandomOperations_MatchPointModel()
    {
        Operation.Array[0, 120].Sample(ops =>
        {
            var set = new RangeSet<int>();
            bool[] model = new bool[Universe];
            foreach ((int op, int start, int length) in ops)
            {
                int end = Math.Min(Universe, start + length);
                bool[] range = [.. Enumerable.Range(0, Universe).Select(p => p >= start && p < end)];
                switch (op)
                {
                    case 0:
                    case 1:
                        set.Add(start, end).Should().Be(range.Where((inRange, p) => inRange && !model[p]).Any());
                        for (int p = start; p < end; p++) model[p] = true;
                        break;
                    case 2:
                    case 3:
                        set.Remove(start, end).Should().Be(range.Where((inRange, p) => inRange && model[p]).Any());
                        for (int p = start; p < end; p++) model[p] = false;
                        break;
                    case 4:
                        set.Contains(start).Should().Be(model[start]);
                        set.TryGetRange(start, out HalfOpenRange<int> around).Should().Be(model[start]);
                        if (model[start])
                        {
                            RangesOf(model).Should().Contain(around);
                        }

                        break;
                    case 5:
                        set.Contains(start, end).Should().Be(Enumerable.Range(start, end - start).All(p => model[p]));
                        break;
                    default:
                        set.Overlaps(start, end).Should().Be(Enumerable.Range(start, end - start).Any(p => model[p]));
                        break;
                }

                ShouldMatch(set, model);
            }
        }, iter: TestScale.Iterations(2000));
    }

    [Test]
    public void SetAlgebra_MatchesPointModel()
    {
        Gen<HalfOpenRange<int>> range = Gen.Select(Gen.Int[0, Universe - 1], Gen.Int[0, 12]).Select(static t => new HalfOpenRange<int>(t.Item1, Math.Min(Universe, t.Item1 + t.Item2)));
        Gen.Select(range.Array[0, 10], range.Array[0, 10]).Sample(t =>
        {
            (HalfOpenRange<int>[] left, HalfOpenRange<int>[] right) = t;
            bool[] a = Points(left);
            bool[] b = Points(right);

            var union = new RangeSet<int>(left);
            union.UnionWith(new RangeSet<int>(right));
            ShouldMatch(union, [.. a.Zip(b, static (x, y) => x || y)]);

            var intersection = new RangeSet<int>(left);
            intersection.IntersectWith(new RangeSet<int>(right));
            ShouldMatch(intersection, [.. a.Zip(b, static (x, y) => x && y)]);

            var difference = new RangeSet<int>(left);
            difference.ExceptWith(new RangeSet<int>(right));
            ShouldMatch(difference, [.. a.Zip(b, static (x, y) => x && !y)]);

            new RangeSet<int>(left).SetEquals(new RangeSet<int>(right)).Should().Be(a.SequenceEqual(b));
        }, iter: TestScale.Iterations(2000));

        static bool[] Points(IEnumerable<HalfOpenRange<int>> ranges)
        {
            bool[] points = new bool[Universe];
            foreach (HalfOpenRange<int> r in ranges)
            {
                for (int p = r.Start; p < r.End; p++) points[p] = true;
            }

            return points;
        }
    }

    [Test]
    public void Boundaries_AdjacencyNestingAndSplits()
    {
        var set = new RangeSet<int>();
        set.Add(1, 5).Should().BeTrue();
        set.Add(5, 9).Should().BeTrue();
        set.Should().Equal(new HalfOpenRange<int>(1, 9));
        set.Add(2, 4).Should().BeFalse("already covered");
        set.Add(3, 3).Should().BeFalse("an empty range adds nothing");

        set.Remove(3, 4).Should().BeTrue();
        set.Should().Equal(new HalfOpenRange<int>(1, 3), new HalfOpenRange<int>(4, 9));
        set.Contains(3).Should().BeFalse();
        set.Contains(4).Should().BeTrue();
        set.Contains(9).Should().BeFalse("the end is excluded");
        set.Contains(6, 6).Should().BeTrue("an empty range is always contained");
        set.Overlaps(3, 4).Should().BeFalse();
        set.Overlaps(6, 6).Should().BeFalse("an empty range overlaps nothing");
        set.Bounds.Should().Be(new HalfOpenRange<int>(1, 9));
        set.Remove(new HalfOpenRange<int>(0, 100)).Should().BeTrue();
        set.IsEmpty.Should().BeTrue();
        set.Remove(0, 100).Should().BeFalse();
        set.Remove(5, 5).Should().BeFalse();
        set.Add(new HalfOpenRange<int>(0, 1)).Should().BeTrue();
    }

    [Test]
    public void DatesComparersAndValidation()
    {
        var day = new DateTime(2026, 11, 24, 0, 0, 0, DateTimeKind.Utc);
        var booked = new RangeSet<DateTime>([new(day.AddHours(9), day.AddHours(10)), new(day.AddHours(10), day.AddHours(12))]);
        booked.Count.Should().Be(1);
        booked.Overlaps(day.AddHours(11.5), day.AddHours(13)).Should().BeTrue();
        booked.Comparer.Should().BeSameAs(Comparer<DateTime>.Default);

        var descending = new RangeSet<int>(Comparer<int>.Create(static (a, b) => b.CompareTo(a)));
        descending.Add(10, 5);
        descending.Contains(7).Should().BeTrue();
        descending.Contains(5).Should().BeFalse();

        var set = new RangeSet<int>();
        set.Invoking(s => s.Add(5, 1)).Should().Throw<ArgumentException>();
        set.Invoking(s => s.Remove(5, 1)).Should().Throw<ArgumentException>();
        set.Invoking(s => s.Contains(5, 1)).Should().Throw<ArgumentException>();
        set.Invoking(s => s.Bounds).Should().Throw<InvalidOperationException>();
        set.Invoking(s => s.UnionWith(null!)).Should().Throw<ArgumentNullException>();
        set.Invoking(s => s.IntersectWith(null!)).Should().Throw<ArgumentNullException>();
        set.Invoking(s => s.ExceptWith(null!)).Should().Throw<ArgumentNullException>();
        set.Invoking(s => s.SetEquals(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new RangeSet<int>(null!, null)).Should().Throw<ArgumentNullException>();
        new RangeSet<string>().Invoking(s => s.Add(null!, "b")).Should().Throw<ArgumentNullException>();

        set.Add(1, 3);
        set.UnionWith(set);
        set.IntersectWith(set);
        set.Count.Should().Be(1);
        set.ExceptWith(set);
        set.IsEmpty.Should().BeTrue();
        set.Add(1, 3);
        set.SetEquals(new RangeSet<int>([new(1, 2), new(5, 6)])).Should().BeFalse();
        set.SetEquals(new RangeSet<int>([new(1, 4)])).Should().BeFalse();
        set.SetEquals(new RangeSet<int>([new(0, 3)])).Should().BeFalse();

        RangeSet<int>.Enumerator e = set.GetEnumerator();
        e.MoveNext().Should().BeTrue();
        e.Reset();
        e.MoveNext().Should().BeTrue();
        ((System.Collections.IEnumerator)e).Current.Should().Be(new HalfOpenRange<int>(1, 3));
        e.MoveNext().Should().BeFalse();
        e.Dispose();
        ((System.Collections.IEnumerable)set).Cast<HalfOpenRange<int>>().Should().HaveCount(1);
        set.Clear();
        set.Count.Should().Be(0);
    }
}
