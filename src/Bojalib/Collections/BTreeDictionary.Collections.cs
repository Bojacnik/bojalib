using System.Collections;
using System.Diagnostics;
using Bojalib.Collections.Internal;

namespace Bojalib.Collections;

public sealed partial class BTreeDictionary<TKey, TValue>
{
    /// <summary>
    /// A live, read-only view of the keys of a <see cref="BTreeDictionary{TKey, TValue}"/>, in key order. Counting is
    /// O(1) and enumeration allocates nothing.
    /// </summary>
    [DebuggerDisplay("Count = {Count}")]
    public sealed class KeyCollection : ICollection<TKey>, IReadOnlyList<TKey>
    {
        private readonly BTreeDictionary<TKey, TValue> _owner;

        internal KeyCollection(BTreeDictionary<TKey, TValue> owner) => _owner = owner;

        /// <summary>Gets the number of keys.</summary>
        public int Count => _owner.Count;

        bool ICollection<TKey>.IsReadOnly => true;

        /// <summary>Gets the key at <paramref name="index"/> in key order. O(log n).</summary>
        /// <param name="index">The position, 0 for the smallest key.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside [0, Count).</exception>
        public TKey this[int index] => _owner.GetAt(index).Key;

        /// <summary>Returns whether the dictionary contains <paramref name="item"/> as a key, by lookup.</summary>
        /// <param name="item">The key.</param>
        /// <returns><see langword="true"/> if present.</returns>
        public bool Contains(TKey item) => _owner.ContainsKey(item);

        /// <summary>Copies the keys, in key order, to <paramref name="array"/>.</summary>
        /// <param name="array">The destination.</param>
        /// <param name="arrayIndex">Where to start writing.</param>
        /// <exception cref="ArgumentNullException"><paramref name="array"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="arrayIndex"/> is negative.</exception>
        /// <exception cref="ArgumentException">The array is too short.</exception>
        public void CopyTo(TKey[] array, int arrayIndex)
        {
            ArgumentNullException.ThrowIfNull(array);
            ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
            if (array.Length - arrayIndex < Count)
            {
                throw new ArgumentException("The destination array is too short.", nameof(array));
            }

            foreach (KeyValuePair<TKey, TValue> pair in _owner)
            {
                array[arrayIndex++] = pair.Key;
            }
        }

        /// <summary>Enumerates the keys in key order.</summary>
        /// <returns>An allocation-free enumerator.</returns>
        public Enumerator GetEnumerator() => new(_owner.GetEnumerator());

        IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        void ICollection<TKey>.Add(TKey item) => throw ReadOnly();

        void ICollection<TKey>.Clear() => throw ReadOnly();

        bool ICollection<TKey>.Remove(TKey item) => throw ReadOnly();

        private static NotSupportedException ReadOnly() => new("The collection is read-only; change the dictionary instead.");

        /// <summary>Enumerates a <see cref="KeyCollection"/>.</summary>
        public struct Enumerator : IEnumerator<TKey>
        {
            private BTreeDictionary<TKey, TValue>.Enumerator _inner;

            internal Enumerator(BTreeDictionary<TKey, TValue>.Enumerator inner) => _inner = inner;

            /// <summary>Gets the current key.</summary>
            public readonly TKey Current => _inner.Current.Key;

            readonly object? IEnumerator.Current => Current;

            /// <summary>Advances to the next key.</summary>
            /// <returns><see langword="false"/> at the end.</returns>
            public bool MoveNext() => _inner.MoveNext();

            /// <summary>Restarts the enumeration.</summary>
            public void Reset() => _inner.Reset();

            /// <summary>Releases the enumerator.</summary>
            public void Dispose() => _inner.Dispose();
        }
    }

    /// <summary>
    /// A live, read-only view of the values of a <see cref="BTreeDictionary{TKey, TValue}"/>, in key order. Counting is
    /// O(1) and enumeration allocates nothing.
    /// </summary>
    [DebuggerDisplay("Count = {Count}")]
    public sealed class ValueCollection : ICollection<TValue>, IReadOnlyList<TValue>
    {
        private readonly BTreeDictionary<TKey, TValue> _owner;

        internal ValueCollection(BTreeDictionary<TKey, TValue> owner) => _owner = owner;

        /// <summary>Gets the number of values.</summary>
        public int Count => _owner.Count;

        bool ICollection<TValue>.IsReadOnly => true;

        /// <summary>Gets the value at <paramref name="index"/> in key order. O(log n).</summary>
        /// <param name="index">The position, 0 for the smallest key.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside [0, Count).</exception>
        public TValue this[int index] => _owner.GetAt(index).Value;

        /// <summary>Returns whether some entry has <paramref name="item"/> as its value. O(n).</summary>
        /// <param name="item">The value.</param>
        /// <returns><see langword="true"/> if present.</returns>
        public bool Contains(TValue item) => DictionaryInterop.ContainsValue(_owner, item);

        /// <summary>Copies the values, in key order, to <paramref name="array"/>.</summary>
        /// <param name="array">The destination.</param>
        /// <param name="arrayIndex">Where to start writing.</param>
        /// <exception cref="ArgumentNullException"><paramref name="array"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="arrayIndex"/> is negative.</exception>
        /// <exception cref="ArgumentException">The array is too short.</exception>
        public void CopyTo(TValue[] array, int arrayIndex)
        {
            ArgumentNullException.ThrowIfNull(array);
            ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
            if (array.Length - arrayIndex < Count)
            {
                throw new ArgumentException("The destination array is too short.", nameof(array));
            }

            foreach (KeyValuePair<TKey, TValue> pair in _owner)
            {
                array[arrayIndex++] = pair.Value;
            }
        }

        /// <summary>Enumerates the values in key order.</summary>
        /// <returns>An allocation-free enumerator.</returns>
        public Enumerator GetEnumerator() => new(_owner.GetEnumerator());

        IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        void ICollection<TValue>.Add(TValue item) => throw ReadOnly();

        void ICollection<TValue>.Clear() => throw ReadOnly();

        bool ICollection<TValue>.Remove(TValue item) => throw ReadOnly();

        private static NotSupportedException ReadOnly() => new("The collection is read-only; change the dictionary instead.");

        /// <summary>Enumerates a <see cref="ValueCollection"/>.</summary>
        public struct Enumerator : IEnumerator<TValue>
        {
            private BTreeDictionary<TKey, TValue>.Enumerator _inner;

            internal Enumerator(BTreeDictionary<TKey, TValue>.Enumerator inner) => _inner = inner;

            /// <summary>Gets the current value.</summary>
            public readonly TValue Current => _inner.Current.Value;

            readonly object? IEnumerator.Current => Current;

            /// <summary>Advances to the next value.</summary>
            /// <returns><see langword="false"/> at the end.</returns>
            public bool MoveNext() => _inner.MoveNext();

            /// <summary>Restarts the enumeration.</summary>
            public void Reset() => _inner.Reset();

            /// <summary>Releases the enumerator.</summary>
            public void Dispose() => _inner.Dispose();
        }
    }
}
