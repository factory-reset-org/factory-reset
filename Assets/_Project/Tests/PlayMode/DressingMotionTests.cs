using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.World;
using Object = UnityEngine.Object;

namespace ToyFactory.Tests
{
    /// <summary>The turning gears and blinking server lights, and how they stop at the shutdown.</summary>
    public sealed class DressingMotionTests
    {
        readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        DressingSpinner Spinner(float speed, float windDown)
        {
            var go = new GameObject("Gear");
            _created.Add(go);
            go.SetActive(false);
            DressingSpinner spinner = go.AddComponent<DressingSpinner>();
            spinner.Configure(Vector3.forward, speed);
            typeof(DressingSpinner).GetField("windDownSeconds",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(spinner, windDown);
            go.SetActive(true);
            return spinner;
        }

        DressingBlinker Blinker(int lights, float interval)
        {
            var go = new GameObject("Rack");
            _created.Add(go);
            go.SetActive(false);
            var renderers = new Renderer[lights];
            for (int i = 0; i < lights; i++)
            {
                GameObject light = GameObject.CreatePrimitive(PrimitiveType.Cube);
                light.transform.SetParent(go.transform);
                renderers[i] = light.GetComponent<Renderer>();
            }
            DressingBlinker blinker = go.AddComponent<DressingBlinker>();
            blinker.Configure(renderers, interval, 7);
            go.SetActive(true);
            return blinker;
        }

        [UnityTest]
        public IEnumerator AGearTurnsAroundItsAxle()
        {
            DressingSpinner gear = Spinner(90f, 1f);
            yield return new WaitForSeconds(0.2f);
            Vector3 euler = gear.transform.localEulerAngles;
            Assert.That(euler.z, Is.GreaterThan(1f), "It has not turned.");
            Assert.That(euler.x, Is.EqualTo(0f).Within(0.01f));
            Assert.That(euler.y, Is.EqualTo(0f).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator AGearWindsDownToAStopAtTheShutdown()
        {
            DressingSpinner gear = Spinner(90f, 0.2f);
            yield return null;
            CutsceneEvents.RaiseCriticalSignal(CutsceneSignals.FactoryShutdown);
            yield return new WaitForSeconds(0.4f);
            Assert.That(gear.CurrentDegreesPerSecond, Is.EqualTo(0f));
            Quaternion stopped = gear.transform.localRotation;
            yield return new WaitForSeconds(0.1f);
            Assert.That(Quaternion.Angle(stopped, gear.transform.localRotation), Is.EqualTo(0f));
        }

        [UnityTest]
        public IEnumerator OtherSignalsDoNotStopAGear()
        {
            DressingSpinner gear = Spinner(90f, 0.1f);
            yield return null;
            CutsceneEvents.RaiseCriticalSignal(CutsceneSignals.ControlRoomUnlock);
            yield return new WaitForSeconds(0.2f);
            Assert.That(gear.CurrentDegreesPerSecond, Is.EqualTo(90f));
        }

        [UnityTest]
        public IEnumerator ServerLightsFlipWhileTheFactoryRuns()
        {
            DressingBlinker rack = Blinker(12, 0.02f);
            yield return null;
            int first = rack.LitCount;
            bool changed = false;
            for (int frame = 0; frame < 60 && !changed; frame++)
            {
                yield return null;
                changed = rack.LitCount != first;
            }
            Assert.That(changed, "No light flipped.");
        }

        [UnityTest]
        public IEnumerator EveryServerLightGoesDarkAtTheShutdownAndStaysDark()
        {
            DressingBlinker rack = Blinker(12, 0.02f);
            yield return null;
            CutsceneEvents.RaiseCriticalSignal(CutsceneSignals.FactoryShutdown);
            Assert.That(rack.LitCount, Is.EqualTo(0));
            yield return new WaitForSeconds(0.2f);
            Assert.That(rack.LitCount, Is.EqualTo(0));
        }
    }
}
