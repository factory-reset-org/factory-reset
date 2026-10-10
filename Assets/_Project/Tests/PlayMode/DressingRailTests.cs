using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.World;
using Object = UnityEngine.Object;

namespace ToyFactory.Tests
{
    /// <summary>The Assembly Floor's overhead toy rail, and how it winds down at the shutdown.</summary>
    public sealed class DressingRailTests
    {
        // A 4 x 2 m rectangle: 12 m round.
        static readonly Vector3[] Loop =
        {
            new Vector3(0f, 5f, 0f), new Vector3(4f, 5f, 0f), new Vector3(4f, 5f, 2f), new Vector3(0f, 5f, 2f)
        };

        GameObject _rail;

        [TearDown]
        public void TearDown()
        {
            if (_rail != null)
                Object.DestroyImmediate(_rail);
        }

        DressingRail Rail(int carriers, float speed)
        {
            _rail = new GameObject("Rail");
            _rail.SetActive(false);
            var hooks = new Transform[carriers];
            for (int i = 0; i < carriers; i++)
            {
                hooks[i] = new GameObject("Carrier").transform;
                hooks[i].SetParent(_rail.transform, false);
            }
            DressingRail rail = _rail.AddComponent<DressingRail>();
            rail.Configure(Loop, hooks, speed);
            _rail.SetActive(true);
            return rail;
        }

        [Test]
        public void TheLoopLengthIsItsPerimeter() =>
            Assert.That(DressingRail.LoopLength(Loop), Is.EqualTo(12f).Within(1e-4f));

        [Test]
        public void APointRoundTheLoopFollowsItsSidesAndWraps()
        {
            Assert.That(Vector3.Distance(DressingRail.PointAt(Loop, 1f, out Vector3 along), new Vector3(1f, 5f, 0f)), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(along, Vector3.right), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(DressingRail.PointAt(Loop, 5f, out along), new Vector3(4f, 5f, 1f)), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(along, Vector3.forward), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(DressingRail.PointAt(Loop, 13f, out _), DressingRail.PointAt(Loop, 1f, out _)), Is.LessThan(1e-4f),
                "Past the end it carries on round.");
            Assert.That(Vector3.Distance(DressingRail.PointAt(Loop, -1f, out _), new Vector3(0f, 5f, 1f)), Is.LessThan(1e-4f),
                "Negative distances wrap too.");
        }

        [Test]
        public void TheCarriersAreSpacedEvenlyRoundTheLoop()
        {
            DressingRail rail = Rail(4, 0f);
            Transform[] hooks = rail.GetComponentsInChildren<Transform>();
            // hooks[0] is the rail itself; carriers are 3 m apart round the 12 m loop.
            Assert.That(Vector3.Distance(hooks[1].localPosition, new Vector3(0f, 5f, 0f)), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(hooks[2].localPosition, new Vector3(3f, 5f, 0f)), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(hooks[3].localPosition, new Vector3(4f, 5f, 2f)), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(hooks[4].localPosition, new Vector3(1f, 5f, 2f)), Is.LessThan(1e-4f));
        }

        [UnityTest]
        public IEnumerator TheCarriersMoveWhileTheFactoryRuns()
        {
            DressingRail rail = Rail(2, 2f);
            Transform hook = rail.transform.GetChild(0);
            yield return null;
            Vector3 first = hook.localPosition;
            float giveUp = Time.time + 1f;
            while (Vector3.Distance(hook.localPosition, first) < 1e-3f && Time.time < giveUp)
                yield return null;
            Assert.That(Vector3.Distance(hook.localPosition, first), Is.GreaterThan(1e-3f), "The carrier did not move.");
        }

        [UnityTest]
        public IEnumerator AtTheShutdownTheRailWindsDownToAStop()
        {
            DressingRail rail = Rail(2, 2f);
            yield return null;
            CutsceneEvents.RaiseCriticalSignal(CutsceneSignals.FactoryShutdown);
            // The wind-down takes windDownSeconds (2.5 s); allow a little more.
            float giveUp = Time.time + 3.5f;
            while (rail.CurrentMetresPerSecond > 0f && Time.time < giveUp)
                yield return null;
            Assert.That(rail.CurrentMetresPerSecond, Is.EqualTo(0f));
            Vector3 stopped = rail.transform.GetChild(0).localPosition;
            yield return null;
            yield return null;
            Assert.That(Vector3.Distance(rail.transform.GetChild(0).localPosition, stopped), Is.LessThan(1e-5f), "It stays stopped.");
        }
    }
}
