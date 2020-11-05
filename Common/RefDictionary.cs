using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Common.Core
{
    public class RefDictionary<TKey, TValue>
        where TKey : unmanaged
        where TValue : struct
    {
        private TValue[] _items;
        private int[] _freeIndices;

        private int _count;
        private int _freeCount;

        private Dictionary<TKey, int> _idToIndex;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RefDictionary(int capacity)
        {
            this._items = new TValue[capacity];
            this._freeIndices = new int[capacity];
            this._idToIndex = new Dictionary<TKey, int>(capacity);

            this._count = 0;
            this._freeCount = 0;
        }

        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => this._count - this._freeCount;
        }

        public ref TValue this[TKey key]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref this._items[_idToIndex[key]];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            this._idToIndex.Clear();
            this._count = 0;
            this._freeCount = 0;
        }

        public bool ContainsKey(TKey key)
        {
            return this._idToIndex.ContainsKey(key);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(TKey key, in TValue value)
        {
            int index;

            if (!this._idToIndex.ContainsKey(key))
            {
                index = AllocateKey(key);
            }
            else
            {
                index = this._idToIndex[key];
            }

            this._items[index] = value;
        }

        /// <summary>
        /// Allocates key, but the value is undefined. Set it by reference.
        /// </summary>
        public void Add(TKey key)
        {
            if (!this._idToIndex.ContainsKey(key))
            {
                AllocateKey(key);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(TKey key)
        {
            this._freeIndices[_freeCount++] = this._idToIndex[key];
            this._idToIndex.Remove(key);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator()
        {
            return new Enumerator(this._idToIndex, this._items);
        }

        private int AllocateKey(TKey key)
        {
            int index;

            if (this._freeCount > 0)
            {
                index = this._freeIndices[--this._freeCount];
            }
            else
            {
                if (this._count == this._items.Length)
                {
                    Array.Resize(ref _items, 2 * this._count);
                    Array.Resize(ref _freeIndices, 2 * this._count);
                }

                index = this._count++;
            }

            this._idToIndex[key] = index;

            return index;
        }

        public struct Enumerator
        {
            private readonly TValue[] _items;

            private Dictionary<TKey, int>.Enumerator _enumerator;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal Enumerator(Dictionary<TKey, int> dictionary, TValue[] items)
            {
                this._items = items;
                this._enumerator = dictionary.GetEnumerator();
            }

            public ref TValue Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => ref this._items[this._enumerator.Current.Value];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext() => this._enumerator.MoveNext();
        }
    }
}
