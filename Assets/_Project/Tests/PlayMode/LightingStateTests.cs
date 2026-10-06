using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.World;
using Object = UnityEngine.Object;

namespace ToyFactory.Tests
{
    public sealed class LightingStateTests
    {
        static readonly Color Red = new Color(1f, 0.19f, 0.25f);
        static readonly Color Amber = new Color(1f, 0.69f, 0f);
        static readonly Color Warm = new Color(1f, 0.7f, 0.42f);
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        readonly List<Object> _created = new List<Object>();
        Material _on, _off, _amber, _screen;
        Light _alarm, _accent;
        Renderer _beacon, _screenRenderer;

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        T Track<T>(T thing) where T : Object
        {
            _created.Add(thing);
            return thing;
        }

        static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        Material MaterialWithEmission(string name, Color emission)
        {
            Material material = Track(new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name });
            material.EnableKeyword("_EMISSION");
            material.SetColor(EmissionColorId, emission);
            return material;
        }

        Light PointLight(string name, Color colour, float intensity)
        {
            var go = Track(new GameObject(name));
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = colour;
            light.intensity = intensity;
            return light;
        }

        Renderer Cube(string name, Material material)
        {
            GameObject go = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            go.name = name;
            Renderer renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        LightingState Create(float shutdownSeconds = 0.2f)
        {
            _on = MaterialWithEmission("AlarmOn", Red * 2.5f);
            _off = MaterialWithEmission("AlarmOff", Red * 0.3f);
            _amber = MaterialWithEmission("Amber", Amber * 2f);
            _screen = MaterialWithEmission("Screen", Color.cyan * 2f);
            _alarm = PointLight("Alarm", Color.white, 1f);
            _accent = PointLight("Accent", Color.white, 10f);
            _beacon = Cube("Beacon", _on);
            _screenRenderer = Cube("Screen", _screen);

            var go = Track(new GameObject("Lighting"));
            go.SetActive(false);
            LightingState state = go.AddComponent<LightingState>();
            SetField(state, "alarmLights", new[] { _alarm });
            SetField(state, "alarmBeacons", new[] { _beacon });
            SetField(state, "alarmOnMaterial", _on);
            SetField(state, "alarmOffMaterial", _off);
            SetField(state, "unlockedMaterial", _amber);
            SetField(state, "shutdownLights", new[] { _alarm, _accent });
            SetField(state, "shutdownEmissives", new[] { _screenRenderer });
            SetField(state, "shutdownSeconds", shutdownSeconds);
            go.SetActive(true);
            return state;
        }

        static void AssertColour(Color expected, Color actual, string message = null)
        {
            Assert.AreEqual(expected.r, actual.r, 1e-3f, message);
            Assert.AreEqual(expected.g, actual.g, 1e-3f, message);
            Assert.AreEqual(expected.b, actual.b, 1e-3f, message);
        }

        [UnityTest]
        public IEnumerator StartsAsARedAlarm()
        {
            LightingState state = Create();
            yield return null;

            Assert.AreEqual(LightingMode.Alarm, state.Mode);
            AssertColour(Red, _alarm.color);
            Material expected = LightingState.IsBlinkOn(Time.time, 0.95f) ? _on : _off;
            Assert.AreSame(expected, _beacon.sharedMaterial, "The beacon shows the half of the blink that matches the time.");
        }

        [Test]
        public void TheAlarmBlinksAboutOnceASecond()
        {
            Assert.IsTrue(LightingState.IsBlinkOn(0f, 1f));
            Assert.IsTrue(LightingState.IsBlinkOn(0.49f, 1f));
            Assert.IsFalse(LightingState.IsBlinkOn(0.5f, 1f));
            Assert.IsFalse(LightingState.IsBlinkOn(0.99f, 1f));
            Assert.IsTrue(LightingState.IsBlinkOn(1f, 1f));
        }

        [UnityTest]
        public IEnumerator TheUnlockSignalTurnsTheAlarmAmber()
        {
            LightingState state = Create();
            yield return null;

            CutsceneEvents.RaiseCriticalSignal(CutsceneSignals.ControlRoomUnlock);

            Assert.AreEqual(LightingMode.Unlocked, state.Mode);
            AssertColour(Amber, _alarm.color);
            Assert.AreEqual(4f, _alarm.intensity, 1e-4f);
            Assert.AreSame(_amber, _beacon.sharedMaterial);

            yield return new WaitForSeconds(0.6f);
            Assert.AreSame(_amber, _beacon.sharedMaterial, "Steady: no more blinking.");
        }

        [UnityTest]
        public IEnumerator TheShutdownSignalFadesLightsWarmAndDimsEmissives()
        {
            LightingState state = Create(shutdownSeconds: 0.2f);
            CutsceneEvents.RaiseCriticalSignal(CutsceneSignals.ControlRoomUnlock);
            yield return null;

            CutsceneEvents.RaiseCriticalSignal(CutsceneSignals.FactoryShutdown);
            Assert.AreEqual(LightingMode.Shutdown, state.Mode);
            Assert.Less(state.ShutdownProgress, 1f);

            yield return new WaitForSeconds(0.35f);

            Assert.AreEqual(1f, state.ShutdownProgress, 1e-4f);
            AssertColour(Warm, _accent.color, "Real-time lights end warm.");
            Assert.AreEqual(10f * 0.35f, _accent.intensity, 1e-3f);
            Assert.AreEqual(4f * 0.35f, _alarm.intensity, 1e-3f, "The amber alarm fades from where it was.");
            Color emission = _screenRenderer.material.GetColor(EmissionColorId);
            AssertColour(Color.cyan * 2f * 0.15f, emission, "Emissives dim to 15%.");
            AssertColour(Color.cyan * 2f, _screen.GetColor(EmissionColorId), "The shared material asset is untouched.");
        }

        [UnityTest]
        public IEnumerator ShutdownStraightFromTheAlarmStopsTheRedBlink()
        {
            LightingState state = Create();
            yield return null;

            state.SetMode(LightingMode.Shutdown);

            Assert.AreSame(_amber, _beacon.sharedMaterial);
        }

        [UnityTest]
        public IEnumerator TheMoodNeverGoesBackwards()
        {
            LightingState state = Create();
            yield return null;
            CutsceneEvents.RaiseCriticalSignal(CutsceneSignals.FactoryShutdown);

            CutsceneEvents.RaiseCriticalSignal(CutsceneSignals.ControlRoomUnlock);
            state.SetMode(LightingMode.Alarm);

            Assert.AreEqual(LightingMode.Shutdown, state.Mode);
        }

        [UnityTest]
        public IEnumerator OtherSignalsAreIgnored()
        {
            LightingState state = Create();
            yield return null;

            CutsceneEvents.RaiseCriticalSignal(CutsceneSignals.CaptainWake);
            CutsceneEvents.RaiseCriticalSignal(CutsceneSignals.CoreShieldsDown);

            Assert.AreEqual(LightingMode.Alarm, state.Mode);
        }

        [UnityTest]
        public IEnumerator ADisabledStateIgnoresSignals()
        {
            LightingState state = Create();
            yield return null;
            state.enabled = false;

            CutsceneEvents.RaiseCriticalSignal(CutsceneSignals.ControlRoomUnlock);

            Assert.AreEqual(LightingMode.Alarm, state.Mode);
        }
    }
}
