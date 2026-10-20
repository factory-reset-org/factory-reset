using System;
using System.Collections.Generic;
using UnityEngine;

namespace ToyFactory.Runtime.Pooling
{
    /// <summary>
    /// Keeps spent copies of a prefab to hand out again, so something that appears many times
    /// a second (a shot, a spark burst) creates objects only until the pool has grown to the
    /// most that are ever out at once, and never after.
    /// </summary>
    /// <remarks>
    /// <see cref="Get"/> returns an active object and <see cref="Release"/> puts it away. The
    /// pool does not release anything itself: the object tells its owner when it is finished.
    /// An item that was destroyed while it sat in the pool (its scene unloaded) is skipped.
    /// </remarks>
    public sealed class ObjectPool<T> where T : Component
    {
        readonly T _prefab;
        readonly Transform _parent;
        readonly Action<T> _onCreate;
        readonly Stack<T> _free = new Stack<T>();

        /// <param name="prefab">What to copy.</param>
        /// <param name="parent">Where the copies live. May be null.</param>
        /// <param name="onCreate">Called once for every new copy, before it is first handed out: the place to subscribe to its events.</param>
        public ObjectPool(T prefab, Transform parent = null, Action<T> onCreate = null)
        {
            if (prefab == null)
                throw new ArgumentNullException(nameof(prefab));
            _prefab = prefab;
            _parent = parent;
            _onCreate = onCreate;
        }

        /// <summary>Copies made so far, in the pool or out.</summary>
        public int Created { get; private set; }

        /// <summary>Copies waiting to be handed out.</summary>
        public int FreeCount => _free.Count;

        /// <summary>Copies currently out.</summary>
        public int ActiveCount => Created - _free.Count;

        /// <summary>Makes copies up front until <paramref name="count"/> exist, so the first shots do not pay for them.</summary>
        public void Prewarm(int count)
        {
            while (Created < count)
                _free.Push(Create());
        }

        /// <summary>An active copy: a spare one if there is one, a new one if not.</summary>
        public T Get()
        {
            T item = null;
            while (item == null && _free.Count > 0)
                item = _free.Pop();   // a destroyed spare reads as null and is dropped
            if (item == null)
                item = Create();

            item.gameObject.SetActive(true);
            return item;
        }

        /// <summary>Puts a copy away. Ignored if it is already away or has been destroyed.</summary>
        public void Release(T item)
        {
            if (item == null || !item.gameObject.activeSelf)
                return;

            item.gameObject.SetActive(false);
            _free.Push(item);
        }

        T Create()
        {
            T item = UnityEngine.Object.Instantiate(_prefab, _parent);
            item.gameObject.SetActive(false);
            Created++;
            _onCreate?.Invoke(item);
            return item;
        }
    }
}
