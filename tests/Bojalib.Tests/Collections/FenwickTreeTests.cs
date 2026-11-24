using Bojalib.Collections;
using CsCheck;

namespace Bojalib.Tests.Collections;

public class FenwickTreeTests
{
    [Test]
    public void RandomOperations_MatchArrayModel()
    {
        Gen.Select(Gen.Int[0, 60].SelectMany(n => Gen.Long[-1000, 1000].Array[n]), Gen.Select(Gen.Int[0, 5], Gen.Int[0, 59], Gen.Int[0, 60], Gen.Long[-1000, 1000]).Array[0, 200]).Sample(t =>
        {
            (long[] initial, var ops) = t;
            long[] model = [.. initial];
            var tree = new FenwickTree<long>(initial);
            int n = model.Length;
            foreach ((int op, int a, int b, long value) in ops)
            {
                switch (op)
                {
                    case 0 when n > 0:
                        tree.Add(a % n, value);
                        model[a % n] += value;
                        break;
                    case 1 when n > 0:
                        tree[a % n] = value;
                        model[a % n] = value;
                        break;
                    case 2:
                        int count = b % (n + 1);
                        tree.PrefixSum(count).Should().Be(model.Take(count).Sum());
                        break;
                    case 3:
                        int start = a % (n + 1);
                        int length = b % (n - start + 1);
                        tree.Sum(start, length).Should().Be(model.Skip(start).Take(length).Sum());
                        break;
                    case 4 when n > 0:
                        tree[a % n].Should().Be(model[a % n]);
                        break;
                    default:
                        tree.Total.Should().Be(model.Sum());
                        break;
                }
            }

            tree.ToArray().Should().Equal(model);
            tree.Length.Should().Be(n);
        }, iter: TestScale.Iterations(2000));
    }

    [Test]
    public void LowerAndUpperBound_MatchLinearScan()
    {
        Gen.Select(Gen.Int[0, 40].SelectMany(n => Gen.Int[0, 5].Array[n]), Gen.Int[-2, 120]).Sample(t =>
        {
            (int[] weights, int target) = t;
            var tree = new FenwickTree<int>(weights);
            int lower = weights.Length;
            int upper = weights.Length;
            int sum = 0;
            for (int i = weights.Length - 1; i >= 0; i--)
            {
                // Scan from the right so the smallest qualifying index wins.
                sum = weights.Take(i + 1).Sum();
                if (sum >= target)
                {
                    lower = i;
                }

                if (sum > target)
                {
                    upper = i;
                }
            }

            tree.LowerBound(target).Should().Be(lower);
            tree.UpperBound(target).Should().Be(upper);
        }, iter: TestScale.Iterations(3000));
    }

    [Test]
    public void WeightedSampling_FollowsTheWeights()
    {
        var tree = new FenwickTree<double>([1, 0, 3, 6]);
        var random = new Random(7);
        int[] hits = new int[4];
        for (int i = 0; i < 100_000; i++)
        {
            hits[tree.UpperBound(random.NextDouble() * tree.Total)]++;
        }

        hits[1].Should().Be(0, "a zero weight is never picked");
        (hits[0] / 100_000.0).Should().BeApproximately(0.1, 0.01);
        (hits[2] / 100_000.0).Should().BeApproximately(0.3, 0.01);
        (hits[3] / 100_000.0).Should().BeApproximately(0.6, 0.01);
    }

    [Test]
    public void ArgumentChecksAndEmptyTree()
    {
        var empty = new FenwickTree<int>(0);
        empty.Length.Should().Be(0);
        empty.Total.Should().Be(0);
        empty.LowerBound(1).Should().Be(0);
        empty.ToArray().Should().BeEmpty();

        var tree = new FenwickTree<int>(5);
        tree.ToArray().Should().Equal(0, 0, 0, 0, 0);
        tree.Add(2, 7);
        tree.Invoking(t => t.Add(5, 1)).Should().Throw<ArgumentOutOfRangeException>();
        tree.Invoking(t => t.Add(-1, 1)).Should().Throw<ArgumentOutOfRangeException>();
        tree.Invoking(t => t[5]).Should().Throw<ArgumentOutOfRangeException>();
        tree.Invoking(t => t[-1] = 1).Should().Throw<ArgumentOutOfRangeException>();
        tree.Invoking(t => t.PrefixSum(6)).Should().Throw<ArgumentOutOfRangeException>();
        tree.Invoking(t => t.PrefixSum(-1)).Should().Throw<ArgumentOutOfRangeException>();
        tree.Invoking(t => t.Sum(-1, 1)).Should().Throw<ArgumentOutOfRangeException>();
        tree.Invoking(t => t.Sum(0, -1)).Should().Throw<ArgumentOutOfRangeException>();
        tree.Invoking(t => t.Sum(3, 3)).Should().Throw<ArgumentOutOfRangeException>();
        tree.Invoking(t => t.CopyTo(new int[4])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new FenwickTree<int>(-1)).Should().Throw<ArgumentOutOfRangeException>();

        int[] destination = new int[6];
        tree.CopyTo(destination);
        destination.Should().Equal(0, 0, 7, 0, 0, 0);
        tree.Clear();
        tree.Total.Should().Be(0);
    }

    [Test]
    public void DecimalAndOverflowFollowTheNumberType()
    {
        var money = new FenwickTree<decimal>([0.1m, 0.2m, 0.3m]);
        money.Total.Should().Be(0.6m);

        var wrapping = new FenwickTree<int>([int.MaxValue, 1]);
        wrapping.Total.Should().Be(int.MinValue, "integer sums wrap like a plain unchecked loop");
    }
}
