using System;
using NUnit.Framework;
using ToyFactory.AI.Core.Collections;

namespace ToyFactory.Tests.EditMode
{
    public class BinaryHeapTests
    {
        [Test]
        public void PopsInAscendingPriorityOrder()
        {
            var heap = new BinaryHeap(10);
            heap.Push(3, 5f);
            heap.Push(1, 1f);
            heap.Push(4, 8f);
            heap.Push(2, 3f);

            Assert.AreEqual(1, heap.Pop());
            Assert.AreEqual(2, heap.Pop());
            Assert.AreEqual(3, heap.Pop());
            Assert.AreEqual(4, heap.Pop());
        }

        [Test]
        public void DecreaseKeyMovesItemEarlier()
        {
            var heap = new BinaryHeap(10);
            heap.Push(1, 1f);
            heap.Push(2, 10f);
            heap.Push(3, 20f);

            heap.DecreaseKey(3, 0.5f);

            Assert.AreEqual(3, heap.Pop());
            Assert.AreEqual(1, heap.Pop());
            Assert.AreEqual(2, heap.Pop());
        }

        [Test]
        public void DecreaseKeyToAHigherPriorityThrows()
        {
            var heap = new BinaryHeap(10);
            heap.Push(1, 1f);

            Assert.Throws<ArgumentOutOfRangeException>(() => heap.DecreaseKey(1, 5f));
        }

        [Test]
        public void PushingAnItemAlreadyInTheHeapThrows()
        {
            var heap = new BinaryHeap(10);
            heap.Push(1, 1f);

            Assert.Throws<InvalidOperationException>(() => heap.Push(1, 2f));
        }

        [Test]
        public void PopOnAnEmptyHeapThrows()
        {
            var heap = new BinaryHeap(10);

            Assert.Throws<InvalidOperationException>(() => heap.Pop());
        }

        [Test]
        public void ClearResetsTheHeapForReuse()
        {
            var heap = new BinaryHeap(10);
            heap.Push(1, 1f);
            heap.Push(2, 2f);

            heap.Clear();

            Assert.AreEqual(0, heap.Count);
            Assert.IsFalse(heap.Contains(1));
            Assert.IsFalse(heap.Contains(2));

            heap.Push(1, 5f);
            heap.Push(2, 1f);
            Assert.AreEqual(2, heap.Pop());
            Assert.AreEqual(1, heap.Pop());
        }

        [Test]
        public void ContainsReflectsCurrentHeapMembership()
        {
            var heap = new BinaryHeap(10);
            Assert.IsFalse(heap.Contains(1));

            heap.Push(1, 1f);
            Assert.IsTrue(heap.Contains(1));

            heap.Pop();
            Assert.IsFalse(heap.Contains(1));
        }
    }
}
