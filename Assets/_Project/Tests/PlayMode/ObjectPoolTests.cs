using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.Runtime.Pooling;
using Object = UnityEngine.Object;

namespace ToyFactory.Tests
{
    public sealed class ObjectPoolTests
    {
        sealed class Copy : MonoBehaviour
        {
        }

        readonly List<Object> _created = new List<Object>();
        Copy _template;
        Transform _parent;

        [SetUp]
        public void SetUp()
        {
            _template = Track(new GameObject("Template").AddComponent<Copy>());
            _parent = Track(new GameObject("Parent")).transform;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
                if (created != null)
                    Object.Destroy(created);
            _created.Clear();
        }

        T Track<T>(T item) where T : Object
        {
            _created.Add(item);
            return item;
        }

        ObjectPool<Copy> Pool(Action<Copy> onCreate = null) => new ObjectPool<Copy>(_template, _parent, onCreate);

        [Test]
        public void GetMakesAnActiveCopyWhenNoneIsFree()
        {
            ObjectPool<Copy> pool = Pool();

            Copy item = pool.Get();

            Assert.IsTrue(item.gameObject.activeSelf);
            Assert.AreNotSame(_template, item);
            Assert.AreEqual(1, pool.Created);
            Assert.AreEqual(1, pool.ActiveCount);
            Assert.AreEqual(0, pool.FreeCount);
            Assert.AreSame(_parent, item.transform.parent);
        }

        [Test]
        public void AReleasedCopyIsPutAwayAndHandedBackAgain()
        {
            ObjectPool<Copy> pool = Pool();
            Copy first = pool.Get();

            pool.Release(first);

            Assert.IsFalse(first.gameObject.activeSelf);
            Assert.AreEqual(1, pool.FreeCount);
            Assert.AreEqual(0, pool.ActiveCount);

            Copy second = pool.Get();
            Assert.AreSame(first, second);
            Assert.IsTrue(second.gameObject.activeSelf);
            Assert.AreEqual(1, pool.Created, "Reusing it made no new copy.");
        }

        [Test]
        public void ThePoolOnlyGrowsToTheMostThatAreOutAtOnce()
        {
            ObjectPool<Copy> pool = Pool();

            for (int round = 0; round < 20; round++)
            {
                Copy a = pool.Get();
                Copy b = pool.Get();
                Copy c = pool.Get();
                pool.Release(a);
                pool.Release(b);
                pool.Release(c);
            }

            Assert.AreEqual(3, pool.Created);
            Assert.AreEqual(3, pool.FreeCount);
        }

        [Test]
        public void PrewarmMakesInactiveCopiesUpToTheCountAndNoMore()
        {
            ObjectPool<Copy> pool = Pool();

            pool.Prewarm(4);
            pool.Prewarm(4);

            Assert.AreEqual(4, pool.Created);
            Assert.AreEqual(4, pool.FreeCount);
            Assert.AreEqual(0, pool.ActiveCount);
            for (int i = 0; i < _parent.childCount; i++)
                Assert.IsFalse(_parent.GetChild(i).gameObject.activeSelf);
        }

        [Test]
        public void OnCreateRunsOncePerNewCopyAndNotOnReuse()
        {
            var seen = new List<Copy>();
            ObjectPool<Copy> pool = Pool(seen.Add);

            Copy a = pool.Get();
            pool.Release(a);
            pool.Get();
            pool.Get();

            Assert.AreEqual(2, seen.Count);
            Assert.AreSame(a, seen[0]);
        }

        [Test]
        public void ReleasingTwiceOrReleasingNullPutsNothingExtraAway()
        {
            ObjectPool<Copy> pool = Pool();
            Copy item = pool.Get();

            pool.Release(item);
            pool.Release(item);
            pool.Release(null);

            Assert.AreEqual(1, pool.FreeCount);
            Assert.AreNotSame(pool.Get(), pool.Get(), "Two Gets give two different copies.");
        }

        [UnityTest]
        public IEnumerator ASpareThatWasDestroyedIsSkipped()
        {
            ObjectPool<Copy> pool = Pool();
            Copy gone = pool.Get();
            pool.Release(gone);
            Object.Destroy(gone.gameObject);
            yield return null;

            Copy next = pool.Get();

            Assert.IsTrue(next != null);
            Assert.AreNotSame(gone, next);
            Assert.IsTrue(next.gameObject.activeSelf);
            Assert.AreEqual(2, pool.Created);
        }

        [Test]
        public void ANullPrefabIsRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new ObjectPool<Copy>(null));
        }
    }
}
