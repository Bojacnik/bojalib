using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Bojalib.Text;

/// <summary>Creates <see cref="AhoCorasick{TValue}"/> automata whose value is the pattern itself.</summary>
public static class AhoCorasick
{
    /// <summary>Compiles an automaton that finds every occurrence of any of <paramref name="patterns"/>.</summary>
    /// <param name="patterns">The patterns; each match reports its pattern as the value.</param>
    /// <returns>The automaton.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="patterns"/> or one of them is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A pattern is empty.</exception>
    public static AhoCorasick<string> Create(IEnumerable<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        return new AhoCorasick<string>(patterns.Select(static p => new KeyValuePair<string, string>(p, p)));
    }
}

/// <summary>
/// A compiled, immutable Aho-Corasick automaton: finds every occurrence of any of many patterns in a single pass over
/// the text, in time proportional to the text length plus the number of matches, however many patterns there are.
/// </summary>
/// <remarks>
/// <para>
/// Use it to scan logs, documents or source code for thousands of known keywords, tokens, signatures or banned
/// words at once. <see cref="System.Buffers.SearchValues{T}"/> (for strings) answers "where is the first match of
/// any pattern?"; this automaton reports <em>every</em> match, including overlapping ones, with which pattern
/// matched and its payload.
/// </para>
/// <para>
/// Matching is ordinal (char by char). Matches are reported in order of their end position; matches that end at the
/// same position come longest first, and identical patterns in the order they were given. Matching allocates
/// nothing.
/// </para>
/// <para>
/// Compiling takes O(total pattern length × distinct chars in the patterns). The automaton is a deterministic
/// state table over a compact alphabet (only the chars that occur in some pattern), so each text char costs one
/// table lookup. For very large pattern sets whose table would exceed about 16 MB it switches to a sparse layout
/// with failure links, which is smaller but slower per char. Immutable, so it is safe to share between threads.
/// </para>
/// </remarks>
/// <typeparam name="TValue">The payload reported with each match.</typeparam>
[DebuggerDisplay("Patterns = {PatternCount}, States = {StateCount}")]
public sealed class AhoCorasick<TValue>
{
    private const int MaxDenseEntries = 4 << 20; // 16 MB of int transitions

    private readonly int[] _classOfLatin1;          // char class for chars < 256
    private readonly Dictionary<char, int>? _classOfOther; // char class for other chars, if any pattern uses them
    private readonly int _classCount;               // includes class 0 = "in no pattern"

    // Dense layout: _delta[state * _classCount + class] = next state * _classCount, sign bit set if it reports matches.
    private readonly int[]? _delta;

    // Sparse layout: per-state children sorted by class, plus failure links.
    private readonly int[]? _childStart;   // state -> start in _childClass/_childTarget; length StateCount + 1
    private readonly int[]? _childClass;
    private readonly int[]? _childTarget;
    private readonly int[]? _fail;

    private readonly int[] _outputStart;    // state -> start in _outputPatterns; length StateCount + 1
    private readonly int[] _outputPatterns; // pattern indices ending exactly at each state
    private readonly int[] _dictionaryLink; // state -> nearest proper suffix state with outputs, or -1
    private readonly int[] _firstOutput;    // state -> itself if it has outputs, else its dictionary link (-1 = none)
    private readonly int[] _patternLength;
    private readonly TValue[] _values;

    /// <summary>Compiles an automaton that finds every occurrence of any of the <paramref name="patterns"/>.</summary>
    /// <param name="patterns">Each pattern with the value to report when it matches. Duplicate patterns are allowed and each reports its own value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="patterns"/> or a pattern is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A pattern is empty.</exception>
    public AhoCorasick(IEnumerable<KeyValuePair<string, TValue>> patterns)
        : this(patterns, forceSparse: false)
    {
    }

    /// <summary>Tests use <paramref name="forceSparse"/> to exercise the large-pattern-set layout on small inputs.</summary>
    internal AhoCorasick(IEnumerable<KeyValuePair<string, TValue>> patterns, bool forceSparse)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        KeyValuePair<string, TValue>[] entries = [.. patterns];
        _values = new TValue[entries.Length];
        _patternLength = new int[entries.Length];

        // 1. Alphabet: classes numbered by first appearance; 0 means "not in any pattern".
        int classCount = 1;
        int[] latin1Class = new int[256];
        Dictionary<char, int>? other = null;
        foreach (KeyValuePair<string, TValue> entry in entries)
        {
            string pattern = entry.Key ?? throw new ArgumentNullException(nameof(patterns), "A pattern is null.");
            if (pattern.Length == 0)
            {
                throw new ArgumentException("Patterns must not be empty.", nameof(patterns));
            }

            foreach (char c in pattern)
            {
                if (c < 256)
                {
                    if (latin1Class[c] == 0)
                    {
                        latin1Class[c] = classCount++;
                    }
                }
                else
                {
                    other ??= [];
                    if (!other.ContainsKey(c))
                    {
                        other[c] = classCount++;
                    }
                }
            }
        }

        _classCount = classCount;
        _classOfLatin1 = latin1Class;
        _classOfOther = other;

        // 2. Trie of the patterns, with children in per-state dictionaries while building.
        var children = new List<Dictionary<int, int>?> { null };
        var outputs = new List<List<int>?> { null };
        for (int p = 0; p < entries.Length; p++)
        {
            string pattern = entries[p].Key;
            _values[p] = entries[p].Value;
            _patternLength[p] = pattern.Length;
            int state = 0;
            foreach (char c in pattern)
            {
                int cls = c < 256 ? latin1Class[c] : other![c];
                Dictionary<int, int> map = children[state] ??= [];
                if (!map.TryGetValue(cls, out int next))
                {
                    next = children.Count;
                    map[cls] = next;
                    children.Add(null);
                    outputs.Add(null);
                }

                state = next;
            }

            (outputs[state] ??= []).Add(p);
        }

        int stateCount = children.Count;

        // 3. Failure and dictionary links, breadth first so a state's failure target is finished before it.
        int[] fail = new int[stateCount];
        _dictionaryLink = new int[stateCount];
        _dictionaryLink[0] = -1;
        int[] order = new int[stateCount];
        int head = 0;
        int tail = 0;
        order[tail++] = 0;
        while (head < tail)
        {
            int state = order[head++];
            if (children[state] is not { } map)
            {
                continue;
            }

            foreach ((int cls, int child) in map)
            {
                int f = state == 0 ? 0 : Goto(children, fail, fail[state], cls);
                fail[child] = f;
                _dictionaryLink[child] = outputs[f] is not null ? f : _dictionaryLink[f];
                order[tail++] = child;
            }
        }

        // 4. Outputs, flattened.
        _outputStart = new int[stateCount + 1];
        var flat = new List<int>();
        for (int s = 0; s < stateCount; s++)
        {
            _outputStart[s] = flat.Count;
            if (outputs[s] is { } list)
            {
                flat.AddRange(list);
            }
        }

        _outputStart[stateCount] = flat.Count;
        _outputPatterns = [.. flat];

        // States from which matches are reported: their own outputs, else their dictionary link.
        _firstOutput = new int[stateCount];
        for (int state = 0; state < stateCount; state++)
        {
            _firstOutput[state] = _outputStart[state] != _outputStart[state + 1] ? state : _dictionaryLink[state];
        }

        // 5. Transitions: a full table when it fits, otherwise sparse children plus failure links.
        if (!forceSparse && (long)stateCount * classCount <= MaxDenseEntries)
        {
            int[] delta = new int[stateCount * classCount];
            foreach (int state in order)
            {
                Span<int> row = delta.AsSpan(state * classCount, classCount);
                if (state != 0)
                {
                    // Missing transitions behave like the failure state's, which is shallower and already filled.
                    delta.AsSpan(fail[state] * classCount, classCount).CopyTo(row);
                }

                if (children[state] is { } map)
                {
                    foreach ((int cls, int child) in map)
                    {
                        row[cls] = child;
                    }
                }
            }

            // Store each target pre-multiplied by the row length, with the sign bit set when the target reports
            // matches: the scan loop then needs one load per char and no multiply.
            for (int i = 0; i < delta.Length; i++)
            {
                int target = delta[i];
                delta[i] = (target * classCount) | (_firstOutput[target] >= 0 ? int.MinValue : 0);
            }

            _delta = delta;
        }
        else
        {
            _fail = fail;
            _childStart = new int[stateCount + 1];
            var childClass = new List<int>();
            var childTarget = new List<int>();
            for (int s = 0; s < stateCount; s++)
            {
                _childStart[s] = childClass.Count;
                if (children[s] is { } map)
                {
                    foreach (int cls in map.Keys.Order())
                    {
                        childClass.Add(cls);
                        childTarget.Add(map[cls]);
                    }
                }
            }

            _childStart[stateCount] = childClass.Count;
            _childClass = [.. childClass];
            _childTarget = [.. childTarget];
        }

        StateCount = stateCount;
    }

    /// <summary>Gets the number of patterns.</summary>
    public int PatternCount => _values.Length;

    /// <summary>Gets the number of automaton states (at most the total pattern length plus one).</summary>
    public int StateCount { get; }

    /// <summary>Gets whether the automaton uses the compact sparse layout for very large pattern sets.</summary>
    internal bool IsSparse => _delta is null;

    /// <summary>Returns whether any pattern occurs in <paramref name="text"/>, stopping at the first match.</summary>
    /// <param name="text">The text to search.</param>
    /// <returns><see langword="true"/> if some pattern occurs.</returns>
    public bool ContainsAny(ReadOnlySpan<char> text) => TryFindFirst(text, out _);

    /// <summary>Finds the match that ends first in <paramref name="text"/> (the longest one, if several end there).</summary>
    /// <param name="text">The text to search.</param>
    /// <param name="match">The match.</param>
    /// <returns><see langword="true"/> if some pattern occurs.</returns>
    public bool TryFindFirst(ReadOnlySpan<char> text, out Match match)
    {
        MatchEnumerator matches = EnumerateMatches(text);
        if (matches.MoveNext())
        {
            match = matches.Current;
            return true;
        }

        match = default;
        return false;
    }

    /// <summary>Counts every match in <paramref name="text"/>, overlapping ones included.</summary>
    /// <param name="text">The text to search.</param>
    /// <returns>The number of matches.</returns>
    public int CountMatches(ReadOnlySpan<char> text)
    {
        int count = 0;
        foreach (Match _ in EnumerateMatches(text))
        {
            count++;
        }

        return count;
    }

    /// <summary>Enumerates every match in <paramref name="text"/>, overlapping ones included, without allocating.</summary>
    /// <param name="text">The text to search.</param>
    /// <returns>An enumerator for <c>foreach</c>.</returns>
    public MatchEnumerator EnumerateMatches(ReadOnlySpan<char> text) => new(this, text);

    private int ClassOfOther(char c) => _classOfOther is not null && _classOfOther.TryGetValue(c, out int cls) ? cls : 0;

    // Consumes text from `position` until reaching a state that reports matches, and returns it; returns -1 at the
    // end of the text. `state` is the automaton state after the consumed chars.
    private int Scan(ReadOnlySpan<char> text, ref int position, ref int state)
    {
        int i = position;
        if (_delta is { } delta)
        {
            int rowLength = _classCount;
            ref int table = ref MemoryMarshal.GetArrayDataReference(delta);
            ref int latin1 = ref MemoryMarshal.GetArrayDataReference(_classOfLatin1);
            int offset = state * rowLength;
            while ((uint)i < (uint)text.Length)
            {
                char c = text[i++];
                int cls = c < 256 ? Unsafe.Add(ref latin1, c) : ClassOfOther(c);
                int entry = Unsafe.Add(ref table, (uint)(offset + cls));
                offset = entry & int.MaxValue;
                if (entry < 0)
                {
                    position = i;
                    state = offset / rowLength;
                    return state;
                }
            }

            position = i;
            state = offset / rowLength;
            return -1;
        }

        int current = state;
        while ((uint)i < (uint)text.Length)
        {
            char c = text[i++];
            current = NextSparse(current, c < 256 ? _classOfLatin1[c] : ClassOfOther(c));
            if (_firstOutput[current] >= 0)
            {
                position = i;
                state = current;
                return current;
            }
        }

        position = i;
        state = current;
        return -1;
    }

    private int NextSparse(int state, int cls)
    {
        while (true)
        {
            ReadOnlySpan<int> classes = _childClass.AsSpan(_childStart![state], _childStart[state + 1] - _childStart[state]);
            int index = classes.BinarySearch(cls);
            if (index >= 0)
            {
                return _childTarget![_childStart[state] + index];
            }

            if (state == 0)
            {
                return 0;
            }

            state = _fail![state];
        }
    }

    private static int Goto(List<Dictionary<int, int>?> children, int[] fail, int state, int cls)
    {
        while (true)
        {
            if (children[state] is { } map && map.TryGetValue(cls, out int next))
            {
                return next;
            }

            if (state == 0)
            {
                return 0;
            }

            state = fail[state];
        }
    }

    /// <summary>One occurrence of a pattern in the text.</summary>
    /// <param name="Index">Where the match starts in the text.</param>
    /// <param name="Length">The match length (the pattern's length).</param>
    /// <param name="PatternIndex">The position of the pattern in the sequence the automaton was built from.</param>
    /// <param name="Value">The pattern's value.</param>
    public readonly record struct Match(int Index, int Length, int PatternIndex, TValue Value);

    /// <summary>Enumerates the matches in a text. Use it with <c>foreach</c>.</summary>
    [StructLayout(LayoutKind.Auto)]
    public ref struct MatchEnumerator
    {
        private readonly AhoCorasick<TValue> _automaton;
        private readonly ReadOnlySpan<char> _text;
        private int _position; // index of the next text char to consume
        private int _state;
        private int _outputState; // state whose outputs are being reported, or -1
        private int _outputIndex; // next index into _outputPatterns for _outputState
        private Match _current;

        internal MatchEnumerator(AhoCorasick<TValue> automaton, ReadOnlySpan<char> text)
        {
            _automaton = automaton;
            _text = text;
            _outputState = -1;
        }

        /// <summary>Gets the current match.</summary>
        public readonly Match Current => _current;

        /// <summary>Returns this enumerator, so it can be used with <c>foreach</c>.</summary>
        /// <returns>This enumerator.</returns>
        public readonly MatchEnumerator GetEnumerator() => this;

        /// <summary>Advances to the next match.</summary>
        /// <returns><see langword="false"/> when there are no more matches.</returns>
        public bool MoveNext()
        {
            AhoCorasick<TValue> a = _automaton;
            while (true)
            {
                // Report pending outputs: the current state's own, then those along its dictionary links.
                while (_outputState >= 0)
                {
                    if (_outputIndex < a._outputStart[_outputState + 1])
                    {
                        int pattern = a._outputPatterns[_outputIndex++];
                        int length = a._patternLength[pattern];
                        _current = new Match(_position - length, length, pattern, a._values[pattern]);
                        return true;
                    }

                    _outputState = a._dictionaryLink[_outputState];
                    if (_outputState >= 0)
                    {
                        _outputIndex = a._outputStart[_outputState];
                    }
                }

                // Consume chars until a state with any output is reached.
                int found = a.Scan(_text, ref _position, ref _state);
                if (found < 0)
                {
                    return false;
                }

                _outputState = a._firstOutput[found];
                _outputIndex = a._outputStart[_outputState];
            }
        }
    }
}
