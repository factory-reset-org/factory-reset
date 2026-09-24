using System;

namespace ToyFactory.AI.Core.Collections
{
    /// <summary>
    /// A min-heap over a fixed range of integer item ids (0..capacity-1), typically flattened
    /// grid cell indices. Supports O(log n) push, pop and decrease-key. Used by A*, GBFS,
    /// Dijkstra and noise propagation.
    /// </summary>
    /// <remarks>
    /// All three backing arrays are allocated once, in the constructor, and reused for every
    /// push/pop/decrease-key, so a single instance can be kept alive across many searches with
    /// zero per-operation GC allocation. <see cref="Clear"/> resets it for reuse.
    /// </remarks>
    public sealed class BinaryHeap
    {
        readonly int[] _heap;       // _heap[i] = item id currently at heap position i
        readonly int[] _position;   // _position[itemId] = heap index, or -1 if not present
        readonly float[] _priority; // _priority[itemId] = current priority for that item

        public BinaryHeap(int capacity)
        {
            _heap = new int[capacity];
            _position = new int[capacity];
            _priority = new float[capacity];
            for (int i = 0; i < capacity; i++) _position[i] = -1;
        }

        /// <summary>Number of items currently in the heap.</summary>
        public int Count { get; private set; }

        public bool IsEmpty => Count == 0;

        /// <summary>True when <paramref name="itemId"/> is currently in the heap.</summary>
        public bool Contains(int itemId) => _position[itemId] >= 0;

        /// <summary>Empties the heap so it can be reused for a fresh search.</summary>
        public void Clear()
        {
            for (int i = 0; i < Count; i++) _position[_heap[i]] = -1;
            Count = 0;
        }

        /// <summary>Adds <paramref name="itemId"/> with the given priority. Item must not already be in the heap.</summary>
        public void Push(int itemId, float priority)
        {
            if (Contains(itemId))
                throw new InvalidOperationException($"Item {itemId} is already in the heap; use DecreaseKey instead.");

            int i = Count++;
            _heap[i] = itemId;
            _position[itemId] = i;
            _priority[itemId] = priority;
            SiftUp(i);
        }

        /// <summary>Removes and returns the item with the smallest priority.</summary>
        public int Pop()
        {
            if (Count == 0)
                throw new InvalidOperationException("Heap is empty.");

            int root = _heap[0];
            _position[root] = -1;

            Count--;
            if (Count > 0)
            {
                int last = _heap[Count];
                _heap[0] = last;
                _position[last] = 0;
                SiftDown(0);
            }

            return root;
        }

        /// <summary>
        /// Lowers the priority of an item already in the heap. <paramref name="newPriority"/>
        /// must not be greater than the item's current priority.
        /// </summary>
        public void DecreaseKey(int itemId, float newPriority)
        {
            int i = _position[itemId];
            if (i < 0)
                throw new InvalidOperationException($"Item {itemId} is not in the heap.");
            if (newPriority > _priority[itemId])
                throw new ArgumentOutOfRangeException(nameof(newPriority), "DecreaseKey cannot increase a priority.");

            _priority[itemId] = newPriority;
            SiftUp(i);
        }

        void SiftUp(int i)
        {
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (_priority[_heap[parent]] <= _priority[_heap[i]]) break;
                Swap(i, parent);
                i = parent;
            }
        }

        void SiftDown(int i)
        {
            while (true)
            {
                int left = 2 * i + 1;
                int right = left + 1;
                int smallest = i;

                if (left < Count && _priority[_heap[left]] < _priority[_heap[smallest]]) smallest = left;
                if (right < Count && _priority[_heap[right]] < _priority[_heap[smallest]]) smallest = right;
                if (smallest == i) break;

                Swap(i, smallest);
                i = smallest;
            }
        }

        void Swap(int a, int b)
        {
            int itemA = _heap[a], itemB = _heap[b];
            _heap[a] = itemB;
            _heap[b] = itemA;
            _position[itemB] = a;
            _position[itemA] = b;
        }
    }
}
