namespace Bojalib.Collections.Internal;

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
