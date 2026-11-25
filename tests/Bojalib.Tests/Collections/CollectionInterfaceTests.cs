using Bojalib.Collections;
using CsCheck;

namespace Bojalib.Tests.Collections;

/// <summary>The BCL interfaces the collections implement, checked against the BCL's own implementations.</summary>
public class CollectionInterfaceTests
{
    private static void DictionaryContract(IDictionary<string, int> dictionary)
    {
        dictionary.IsReadOnly.Should().BeFalse();
        dictionary.Add(new KeyValuePair<string, int>("b", 2));
        dictionary.Add("a", 1);
        dictionary["c"] = 3;
        dictionary.Contains(new("a", 1)).Should().BeTrue();
        dictionary.Contains(new("a", 9)).Should().BeFalse();
        dictionary.Contains(new("z", 1)).Should().BeFalse();
        dictionary.Remove(new KeyValuePair<string, int>("a", 9)).Should().BeFalse("the value differs");
        dictionary.Remove(new KeyValuePair<string, int>("a", 1)).Should().BeTrue();

        ICollection<string> keys = dictionary.Keys;
        keys.Count.Should().Be(2);
        keys.IsReadOnly.Should().BeTrue();
        keys.Contains("b").Should().BeTrue();
        keys.Contains("a").Should().BeFalse();
        keys.Should().Equal("b", "c");
        string[] keyArray = new string[3];
        keys.CopyTo(keyArray, 1);
        keyArray.Should().Equal(null, "b", "c");
        keys.Invoking(k => k.Add("x")).Should().Throw<NotSupportedException>();
        keys.Invoking(k => k.Remove("b")).Should().Throw<NotSupportedException>();
        keys.Invoking(k => k.Clear()).Should().Throw<NotSupportedException>();
        keys.Invoking(k => k.CopyTo(new string[2], 1)).Should().Throw<ArgumentException>();
        keys.Invoking(k => k.CopyTo(null!, 0)).Should().Throw<ArgumentNullException>();
        keys.Invoking(k => k.CopyTo(new string[2], -1)).Should().Throw<ArgumentOutOfRangeException>();
        ((System.Collections.IEnumerable)keys).Cast<string>().Should().HaveCount(2);
        ((IReadOnlyCollection<string>)keys).Count.Should().Be(2);

        ICollection<int> values = dictionary.Values;
        values.Should().Equal(2, 3);
        values.Contains(3).Should().BeTrue();
        values.Contains(1).Should().BeFalse();

        var pairs = new KeyValuePair<string, int>[3];
        dictionary.CopyTo(pairs, 1);
        pairs.Skip(1).Should().Equal(new KeyValuePair<string, int>("b", 2), new KeyValuePair<string, int>("c", 3));
        dictionary.Invoking(d => d.CopyTo(new KeyValuePair<string, int>[2], 1)).Should().Throw<ArgumentException>();
        dictionary.Invoking(d => d.CopyTo(null!, 0)).Should().Throw<ArgumentNullException>();
        dictionary.Invoking(d => d.CopyTo(pairs, -1)).Should().Throw<ArgumentOutOfRangeException>();

        // Works where the BCL expects an IDictionary.
        var copy = new Dictionary<string, int>(dictionary);
        copy.Should().BeEquivalentTo(new Dictionary<string, int> { ["b"] = 2, ["c"] = 3 });
    }

    [Test]
    public void BTreeDictionary_IsAnIDictionary() => DictionaryContract(new BTreeDictionary<string, int>(StringComparer.Ordinal));

    [Test]
    public void Trie_IsAnIDictionary() => DictionaryContract(new Trie<int>());

    [Test]
    public void BTreeSet_SetOperations_MatchSortedSet()
    {
        Gen<int[]> items = Gen.Int[0, 40].Array[0, 30];
        Gen.Select(items, items, Gen.Bool).Sample(t =>
        {
            (int[] left, int[] right, bool rightIsBTreeSet) = t;
            IEnumerable<int> other = rightIsBTreeSet ? BTreeSet.Create<int>(right) : right;

            void Same(Action<ISet<int>> mutate)
            {
                var expected = new SortedSet<int>(left);
                var actual = BTreeSet.Create<int>(left);
                mutate(expected);
                mutate(actual);
                actual.Should().Equal(expected);
                actual.Validate();
            }

            Same(s => s.UnionWith(other));
            Same(s => s.IntersectWith(other));
            Same(s => s.ExceptWith(other));
            Same(s => s.SymmetricExceptWith(other));

            var reference = new SortedSet<int>(left);
            BTreeSet<int> set = BTreeSet.Create<int>(left);
            set.IsSubsetOf(other).Should().Be(reference.IsSubsetOf(other));
            set.IsProperSubsetOf(other).Should().Be(reference.IsProperSubsetOf(other));
            set.IsSupersetOf(other).Should().Be(reference.IsSupersetOf(other));
            set.IsProperSupersetOf(other).Should().Be(reference.IsProperSupersetOf(other));
            set.Overlaps(other).Should().Be(reference.Overlaps(other));
            set.SetEquals(other).Should().Be(reference.SetEquals(other));
        }, iter: TestScale.Iterations(2000));
    }

    [Test]
    public void BTreeSet_InterfaceMembersSelfOperationsAndComparers()
    {
        BTreeSet<int> set = [3, 1, 2];
        ICollection<int> collection = set;
        collection.IsReadOnly.Should().BeFalse();
        collection.Add(4);
        set.Count.Should().Be(4);
        IReadOnlySet<int> readOnly = set;
        readOnly.Contains(4).Should().BeTrue();

        int[] array = new int[5];
        set.CopyTo(array, 1);
        array.Should().Equal(0, 1, 2, 3, 4);
        set.Invoking(s => s.CopyTo(new int[3], 0)).Should().Throw<ArgumentException>();
        set.Invoking(s => s.CopyTo(null!, 0)).Should().Throw<ArgumentNullException>();
        set.Invoking(s => s.CopyTo(array, -1)).Should().Throw<ArgumentOutOfRangeException>();

        set.UnionWith(set);
        set.IntersectWith(set);
        set.Count.Should().Be(4);
        set.SymmetricExceptWith(set);
        set.Count.Should().Be(0);
        set.UnionWith([1, 2]);
        set.ExceptWith(set);
        set.Count.Should().Be(0);
        set.IntersectWith([1]);
        set.Count.Should().Be(0);

        // Membership follows this set's comparer, even when the other set orders differently.
        var ignoreCase = new BTreeSet<string>(StringComparer.OrdinalIgnoreCase) { "Apple", "pear" };
        var ordinal = new BTreeSet<string>(StringComparer.Ordinal) { "APPLE", "PEAR" };
        ignoreCase.SetEquals(ordinal).Should().BeTrue();
        ignoreCase.IsSubsetOf(ordinal).Should().BeTrue();

        foreach (Action<BTreeSet<int>> call in new Action<BTreeSet<int>>[]
        {
            static s => s.UnionWith(null!), static s => s.IntersectWith(null!), static s => s.ExceptWith(null!),
            static s => s.SymmetricExceptWith(null!), static s => s.IsSubsetOf(null!), static s => s.IsProperSubsetOf(null!),
            static s => s.IsSupersetOf(null!), static s => s.IsProperSupersetOf(null!), static s => s.Overlaps(null!),
            static s => s.SetEquals(null!),
        })
        {
            set.Invoking(call).Should().Throw<ArgumentNullException>();
        }
    }

    private static readonly int[] Probes = [0, 7, 250, 499];

    [Test]
    public void KeyAndValueCollections_MatchSortedDictionary()
    {
        Gen.Dictionary(Gen.Int[0, 500], Gen.Int[0, 20])[0, 200].Sample(entries =>
        {
            var reference = new SortedDictionary<int, int>(entries);
            var tree = new BTreeDictionary<int, int>();
            var trie = new Trie<int>();
            foreach ((int key, int value) in entries)
            {
                tree[key] = value;
                trie[key.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)] = value;
            }

            BTreeDictionary<int, int>.KeyCollection keys = tree.Keys;
            keys.Should().Equal(reference.Keys);
            tree.Values.Should().Equal(reference.Values);
            keys.Count.Should().Be(reference.Count);
            tree.Values.Count.Should().Be(reference.Count);
            for (int i = 0; i < reference.Count; i++)
            {
                keys[i].Should().Be(reference.Keys.ElementAt(i));
                tree.Values[i].Should().Be(reference.Values.ElementAt(i));
            }

            trie.Keys.Should().Equal(reference.Keys.Select(static k => k.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)));
            trie.Values.Should().Equal(reference.Values);
            trie.Keys.Count.Should().Be(reference.Count);
            trie.Values.Count.Should().Be(reference.Count);

            foreach (int probe in Probes)
            {
                keys.Contains(probe).Should().Be(reference.ContainsKey(probe));
                tree.Values.Contains(probe).Should().Be(reference.ContainsValue(probe));
#pragma warning disable CA1841 // The point is to test Keys.Contains itself.
                trie.Keys.Contains(probe.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)).Should().Be(reference.ContainsKey(probe));
#pragma warning restore CA1841
                trie.Values.Contains(probe).Should().Be(reference.ContainsValue(probe));
            }
        }, iter: TestScale.Iterations(500));
    }

    [Test]
    public void KeyAndValueCollections_AreLiveViewsWithStructEnumerators()
    {
        var tree = new BTreeDictionary<string, int>(StringComparer.Ordinal) { ["b"] = 2 };
        BTreeDictionary<string, int>.KeyCollection keys = tree.Keys;
        tree.Keys.Should().BeSameAs(keys, "the view is created once");
        tree["a"] = 1;
        keys.Should().Equal("a", "b");
        tree.Invoking(t => t.Keys[2]).Should().Throw<ArgumentOutOfRangeException>();
        tree.Invoking(t => t.Values[-1]).Should().Throw<ArgumentOutOfRangeException>();

        int[] values = new int[3];
        tree.Values.CopyTo(values, 1);
        values.Should().Equal(0, 1, 2);
        tree.Values.Invoking(v => v.CopyTo(new int[1], 0)).Should().Throw<ArgumentException>();
        tree.Values.Invoking(v => v.CopyTo(null!, 0)).Should().Throw<ArgumentNullException>();
        tree.Values.Invoking(v => v.CopyTo(values, -1)).Should().Throw<ArgumentOutOfRangeException>();
        ((ICollection<int>)tree.Values).IsReadOnly.Should().BeTrue();
        ((ICollection<int>)tree.Values).Invoking(v => v.Add(1)).Should().Throw<NotSupportedException>();
        ((ICollection<int>)tree.Values).Invoking(v => v.Remove(1)).Should().Throw<NotSupportedException>();
        ((ICollection<int>)tree.Values).Invoking(v => v.Clear()).Should().Throw<NotSupportedException>();
        ((System.Collections.IEnumerable)tree.Values).Cast<int>().Should().Equal(1, 2);
        ((IReadOnlyDictionary<string, int>)tree).Keys.Should().Equal("a", "b");
        ((IReadOnlyDictionary<string, int>)tree).Values.Should().Equal(1, 2);

        BTreeDictionary<string, int>.KeyCollection.Enumerator e = keys.GetEnumerator();
        e.MoveNext().Should().BeTrue();
        ((System.Collections.IEnumerator)e).Current.Should().Be("a");
        e.Reset();
        e.MoveNext().Should().BeTrue();
        e.Current.Should().Be("a");
        e.Dispose();
        BTreeDictionary<string, int>.ValueCollection.Enumerator v = tree.Values.GetEnumerator();
        v.MoveNext().Should().BeTrue();
        ((System.Collections.IEnumerator)v).Current.Should().Be(1);
        v.Reset();
        v.Dispose();

        var trie = new Trie<int> { ["x"] = 9 };
        Trie<int>.KeyCollection trieKeys = trie.Keys;
        trie["w"] = 8;
        trieKeys.Should().Equal("w", "x");
        trie.Keys.Should().BeSameAs(trieKeys);
        ((IReadOnlyDictionary<string, int>)trie).Keys.Should().Equal("w", "x");
        ((IReadOnlyDictionary<string, int>)trie).Values.Should().Equal(8, 9);
        string[] names = new string[2];
        trieKeys.CopyTo(names, 0);
        names.Should().Equal("w", "x");
        Trie<int>.ValueCollection.Enumerator tv = trie.Values.GetEnumerator();
        tv.MoveNext().Should().BeTrue();
        ((System.Collections.IEnumerator)tv).Current.Should().Be(8);
        tv.Reset();
        tv.Dispose();
        Trie<int>.KeyCollection.Enumerator tk = trieKeys.GetEnumerator();
        tk.MoveNext().Should().BeTrue();
        ((System.Collections.IEnumerator)tk).Current.Should().Be("w");
        tk.Reset();
        tk.Dispose();
        ((System.Collections.IEnumerable)trie.Keys).Cast<string>().Should().HaveCount(2);
    }
}
