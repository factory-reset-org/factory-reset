using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.World;
using Object = UnityEngine.Object;

namespace ToyFactory.Tests
{
    /// <summary>The Assembly presses' pumping piston, and how it settles at the shutdown.</summary>
    public sealed class DressingPistonTests
    {
        GameObject _press;

        [TearDown]
        public void TearDown()
        {
            if (_press != null)
                Object.DestroyImmediate(_press);
        }

        DressingPiston Piston(float strokes)
        {
            _press = new GameObject("Press");
            var piston = new GameObject("Piston");
            piston.transform.SetParent(_press.transform, false);
            piston.SetActive(false);
            DressingPiston component = piston.AddComponent<DressingPiston>();
            component.Configure(1f, 2f, strokes, 0f);
            piston.SetActive(true);
            return component;
        }

        [Test]
        public void TheStrokeStaysBetweenItsTwoHeights()
        {
            for (float t = 0f; t < 4f; t += 0.037f)
            {
                float y = DressingPiston.HeightAt(t, 1f, 2f, 0.7f, 0.3f);
                Assert.That(y, Is.InRange(1f, 2f), $"t = {t}");
            }
        }

        [Test]
        public void TheStrokeRepeatsEveryCycle()
        {
            for (float t = 0f; t < 2f; t += 0.11f)
                Assert.That(DressingPiston.HeightAt(t + 2f, 1f, 2f, 0.5f, 0f),
                    Is.EqualTo(DressingPiston.HeightAt(t, 1f, 2f, 0.5f, 0f)).Within(1e-4f));
        }

        [Test]
        public void ThePhaseShiftsThePressesOutOfStep() =>
            Assert.That(DressingPiston.HeightAt(0.4f, 1f, 2f, 0.5f, 0.25f),
                Is.Not.EqualTo(DressingPiston.HeightAt(0.4f, 1f, 2f, 0.5f, 0f)).Within(1e-3f));

        [UnityTest]
        public IEnumerator ThePistonMovesWhileTheFactoryRuns()
        {
            DressingPiston piston = Piston(2f);
            yield return null;
            float first = piston.transform.localPosition.y;
            bool moved = false;
            for (int frame = 0; frame < 30 && !moved; frame++)
            {
                yield return null;
                moved = Mathf.Abs(piston.transform.localPosition.y - first) > 1e-3f;
            }
            Assert.That(moved, "The piston did not move.");
        }

        [UnityTest]
        public IEnumerator AtTheShutdownThePistonRisesToTheTopAndStops()
        {
            DressingPiston piston = Piston(2f);
            yield return null;
            CutsceneEvents.RaiseCriticalSignal(CutsceneSignals.FactoryShutdown);
            // The rise takes at most settleSeconds (1.5 s); allow twice that.
            float giveUp = Time.time + 3f;
            while (!piston.Stopped && Time.time < giveUp)
                yield return null;
            Assert.That(piston.Stopped, "The piston never settled.");
            Assert.That(piston.transform.localPosition.y, Is.EqualTo(2f).Within(1e-4f));
            yield return new WaitForSeconds(0.1f);
            Assert.That(piston.transform.localPosition.y, Is.EqualTo(2f).Within(1e-4f));
        }
    }
}
