using Bojalib.Text;
using CsCheck;

namespace Bojalib.Tests.Text;

public class AhoCorasickTests
{
    // Every occurrence of every pattern, ordered as documented: by end, longest first, then by pattern index.
    private static List<(int Index, int Length, int Pattern)> BruteForce(string[] patterns, string text)
    {
        var matches = new List<(int, int, int)>();
        for (int p = 0; p < patterns.Length; p++)
        {
            for (int i = 0; i + patterns[p].Length <= text.Length; i++)
            {
                if (string.CompareOrdinal(text, i, patterns[p], 0, patterns[p].Length) == 0)
                {
                    matches.Add((i, patterns[p].Length, p));
                }
            }
        }

        return [.. matches.OrderBy(static m => m.Item1 + m.Item2).ThenByDescending(static m => m.Item2).ThenBy(static m => m.Item3)];
    }

    private static List<(int Index, int Length, int Pattern)> Run(AhoCorasick<int> automaton, string text)
    {
        var matches = new List<(int, int, int)>();
        foreach (AhoCorasick<int>.Match m in automaton.EnumerateMatches(text))
        {
            m.Value.Should().Be(m.PatternIndex * 10, "each match carries its pattern's value");
            matches.Add((m.Index, m.Length, m.PatternIndex));
        }

        return matches;
    }

    private static AhoCorasick<int> Build(string[] patterns, bool sparse) =>
        new(patterns.Select(static (p, i) => new KeyValuePair<string, int>(p, i * 10)), sparse);

    [Test]
    public void RandomPatternsAndTexts_MatchBruteForce([Values(false, true)] bool sparse)
    {
        Gen<string> word = Gen.String[Gen.Char["abc"], 1, 5];
        Gen.Select(word.Array[1, 20], Gen.String[Gen.Char["abcd"], 0, 60]).Sample(t =>
        {
            (string[] patterns, string text) = t;
            AhoCorasick<int> automaton = Build(patterns, sparse);
            automaton.IsSparse.Should().Be(sparse);
            List<(int, int, int)> expected = BruteForce(patterns, text);
            Run(automaton, text).Should().Equal(expected);
            automaton.CountMatches(text).Should().Be(expected.Count);
            automaton.ContainsAny(text).Should().Be(expected.Count > 0);
            if (automaton.TryFindFirst(text, out AhoCorasick<int>.Match first))
            {
                (first.Index, first.Length, first.PatternIndex).Should().Be(expected[0]);
            }
        }, iter: TestScale.Iterations(3000));
    }

    [Test]
    public void OverlapsPrefixesAndDuplicates()
    {
        var automaton = new AhoCorasick<string>(
        [
            new("he", "he"), new("she", "she"), new("his", "his"), new("hers", "hers"), new("he", "he again"),
        ]);
        automaton.PatternCount.Should().Be(5);
        automaton.StateCount.Should().BeGreaterThan(1);

        var found = new List<string>();
        foreach (AhoCorasick<string>.Match m in automaton.EnumerateMatches("ushers"))
        {
            found.Add($"{m.Index}:{m.Value}");
        }

        found.Should().Equal("1:she", "2:he", "2:he again", "2:hers");
        automaton.CountMatches("nothing to see").Should().Be(0);
        automaton.TryFindFirst("xyz", out _).Should().BeFalse();
        automaton.ContainsAny(string.Empty).Should().BeFalse();
    }

    [Test]
    public void NonAsciiAndManyDistinctChars([Values(false, true)] bool sparse)
    {
        // 600 distinct chars across Latin-1, Greek, CJK and a surrogate pair, so classes exceed 255.
        string[] patterns = [.. Enumerable.Range(0, 300).Select(static i => new string([(char)(0x4E00 + i), (char)(0x100 + i)])), "é", "ñandú", "😀!", "Ωμέγα"];
        AhoCorasick<int> automaton = Build(patterns, sparse);
        string text = "Le ñandú dit 😀! et " + patterns[123] + patterns[299] + " Ωμέγα é";
        Run(automaton, text).Should().Equal(BruteForce(patterns, text));
    }

    [Test]
    public void StringFactoryAndValidation()
    {
        AhoCorasick<string> keywords = AhoCorasick.Create(["error", "warn", "fatal"]);
        keywords.EnumerateMatches("warn: error, then fatal error").ToList().Select(static m => m.Value)
            .Should().Equal("warn", "error", "fatal", "error");

        FluentActions.Invoking(() => AhoCorasick.Create(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new AhoCorasick<int>(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => AhoCorasick.Create(["ok", null!])).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => AhoCorasick.Create(["ok", ""])).Should().Throw<ArgumentException>();

        AhoCorasick<string> none = AhoCorasick.Create([]);
        none.PatternCount.Should().Be(0);
        none.ContainsAny("anything").Should().BeFalse();
    }
}

internal static class MatchEnumeratorExtensions
{
    public static List<AhoCorasick<string>.Match> ToList(this AhoCorasick<string>.MatchEnumerator matches)
    {
        var list = new List<AhoCorasick<string>.Match>();
        foreach (AhoCorasick<string>.Match m in matches)
        {
            list.Add(m);
        }

        return list;
    }
}
