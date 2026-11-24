using System.Collections;

namespace Bojalib.Collections.Internal;

/// <summary>
/// A live, read-only <see cref="ICollection{T}"/> over a sequence whose count is known, as returned by
/// <see cref="IDictionary{TKey, TValue}.Keys"/> and <see cref="IDictionary{TKey, TValue}.Values"/>.
/// </summary>
internal sealed class ReadOnlyCollectionView<T>(IEnumerable<T> items, Func<int> count, Func<T, bool> contains)
    : ICollection<T>, IReadOnlyCollection<T>
{
    public int Count => count();

    public bool IsReadOnly => true;

    public bool Contains(T item) => contains(item);

    public void CopyTo(T[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
        if (array.Length - arrayIndex < Count)
        {
            throw new ArgumentException("The destination array is too short.", nameof(array));
        }

        foreach (T item in items)
        {
            array[arrayIndex++] = item;
        }
    }

    public IEnumerator<T> GetEnumerator() => items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Add(T item) => throw ReadOnly();

    public void Clear() => throw ReadOnly();

    public bool Remove(T item) => throw ReadOnly();

    private static NotSupportedException ReadOnly() => new("The collection is read-only.");
}

/// <summary>The <see cref="ICollection{T}"/> members of a dictionary, in terms of its own operations.</summary>
internal static class DictionaryInterop
{
    public static bool Contains<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> dictionary, KeyValuePair<TKey, TValue> item) =>
        dictionary.TryGetValue(item.Key, out TValue? value) && EqualityComparer<TValue>.Default.Equals(value, item.Value);

    public static void CopyTo<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> dictionary, KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
        if (array.Length - arrayIndex < dictionary.Count)
        {
            throw new ArgumentException("The destination array is too short.", nameof(array));
        }

        foreach (KeyValuePair<TKey, TValue> pair in dictionary)
        {
            array[arrayIndex++] = pair;
        }
    }

    public static bool ContainsValue<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> dictionary, TValue value)
    {
        foreach (KeyValuePair<TKey, TValue> pair in dictionary)
        {
            if (EqualityComparer<TValue>.Default.Equals(pair.Value, value))
            {
                return true;
            }
        }

        return false;
    }
}
