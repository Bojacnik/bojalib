using System.Buffers;
using BenchmarkDotNet.Attributes;
using Bojalib.Text;

namespace Bojalib.Benchmarks;

/// <summary>
/// Counting every occurrence of many keywords in 100 KB of text: <see cref="AhoCorasick{TValue}"/> against one
/// <see cref="MemoryExtensions.IndexOf{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/> scan per pattern, and against
/// <see cref="SearchValues{T}"/> (which finds where some pattern starts, then each pattern is checked there).
/// </summary>
[MemoryDiagnoser]
public class AhoCorasickBenchmarks
{
    private string[] _patterns = [];
    private string _text = string.Empty;
    private AhoCorasick<string> _automaton = null!;
    private SearchValues<string> _searchValues = null!;

    [Params(10, 100, 1000)]
    public int PatternCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(42);
        string Word() => new([.. Enumerable.Range(0, random.Next(4, 10)).Select(_ => (char)('a' + random.Next(26)))]);
        _patterns = [.. Enumerable.Range(0, PatternCount).Select(_ => Word()).Distinct(StringComparer.Ordinal)];

        // Random words, with a pattern every 20 words or so.
        var text = new System.Text.StringBuilder();
        while (text.Length < 100_000)
        {
            text.Append(random.Next(20) == 0 ? _patterns[random.Next(_patterns.Length)] : Word()).Append(' ');
        }

        _text = text.ToString();
        _automaton = AhoCorasick.Create(_patterns);
        _searchValues = SearchValues.Create(_patterns, StringComparison.Ordinal);
    }

    [Benchmark(Baseline = true)]
    public int IndexOfPerPattern()
    {
        int count = 0;
        foreach (string pattern in _patterns)
        {
            ReadOnlySpan<char> rest = _text;
            int index;
            while ((index = rest.IndexOf(pattern, StringComparison.Ordinal)) >= 0)
            {
                count++;
                rest = rest[(index + 1)..];
            }
        }

        return count;
    }

    [Benchmark]
    public int SearchValuesThenCheckEachPattern()
    {
        int count = 0;
        ReadOnlySpan<char> text = _text;
        int position = 0;
        int index;
        while ((index = text[position..].IndexOfAny(_searchValues)) >= 0)
        {
            position += index;
            foreach (string pattern in _patterns)
            {
                if (text[position..].StartsWith(pattern, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            position++;
        }

        return count;
    }

    [Benchmark]
    public int AhoCorasickAllMatches() => _automaton.CountMatches(_text);
}
