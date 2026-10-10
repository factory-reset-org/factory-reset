using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Cutscenes;

namespace ToyFactory.Tests
{
    /// <summary>
    /// Unit 047 in the cutscenes: the intro opens with its eyes off and they flicker on at the
    /// close-up, its head turns to the radio, and in every cutscene its key turns and its body
    /// sways about the waist.
    /// </summary>
    public sealed class Unit047MotionTests
    {
        readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            CutsceneEvents.RaiseCutsceneEnded("test");
            foreach (Object created in _created)
                if (created != null)
                    Object.Destroy(created);
            _created.Clear();
        }

        static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        static IEnumerator Seconds(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
                yield return null;
        }

        static Transform Pivot(string name, Transform parent, Vector3 at)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = at;
            return pivot;
        }

        // The stand-in with Unit 047's frozen pivots, no meshes but the eyes.
        Unit047Motion Actor(out Transform body, out Transform head, out Transform key)
        {
            var actor = new GameObject("Unit 047 (cutscene)");
            _created.Add(actor);
            actor.SetActive(false);
            var model = new GameObject("Unit047");
            model.transform.SetParent(actor.transform, false);
            Transform root = Pivot("Unit047_Root", model.transform, Vector3.zero);
            body = Pivot("Body_Pivot", root, new Vector3(0f, 0.77f, 0f));
            head = Pivot("Head_Pivot", body, new Vector3(0f, 0.8f, 0f));
            key = Pivot("WindupKey_Pivot", body, new Vector3(0f, 0.54f, -0.216f));
            Pivot("Arm_L_Pivot", body, new Vector3(-0.431f, 0.71f, 0f));
            Pivot("Arm_R_Pivot", body, new Vector3(0.431f, 0.71f, 0f));
            var eyes = GameObject.CreatePrimitive(PrimitiveType.Cube);
            eyes.name = "Eyes";
            eyes.transform.SetParent(head, false);

            CutsceneActor cutsceneActor = actor.AddComponent<CutsceneActor>();
            SetField(cutsceneActor, "model", model);
            Unit047Motion motion = actor.AddComponent<Unit047Motion>();
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _created.Add(material);
            SetField(motion, "eyeMaterial", material);
            actor.SetActive(true);
            return motion;
        }

        [UnityTest]
        public IEnumerator TheIntroOpensWithItsEyesOffThenTheyFlickerOnAndItLooksToTheRadio()
        {
            Unit047Motion motion = Actor(out _, out Transform head, out _);
            CutsceneEvents.RaiseCutsceneStarted("intro");
            yield return null;
            Assert.AreEqual(0f, motion.EyeLevel, "Dark at the start of the intro.");

            motion.Apply("intro", 4, instant: false);   // the close-up
            yield return Seconds(0.25f);
            Assert.That(motion.EyeLevel, Is.InRange(0f, 0.99f), "Flickering, not snapped on.");
            yield return Seconds(0.5f);
            Assert.AreEqual(1f, motion.EyeLevel, "On.");
            Assert.AreEqual(Unit047Beat.LookAhead, motion.Look);

            motion.Apply("intro", 5, instant: false);   // Pip on the radio
            yield return Seconds(1.5f);
            Assert.AreEqual(Unit047Beat.LookSide, motion.Look);
            Assert.That(motion.HeadAngles.x, Is.EqualTo(35f).Within(1.5f), "Turned to its right.");
            Assert.Less(Quaternion.Angle(head.localRotation, Quaternion.Euler(motion.HeadAngles.y, motion.HeadAngles.x, 0f)), 0.1f);
        }

        [UnityTest]
        public IEnumerator OtherCutscenesStartWithEyesOnWhileTheKeyTurnsAndTheBodySways()
        {
            Unit047Motion motion = Actor(out Transform body, out _, out Transform key);
            CutsceneEvents.RaiseCutsceneStarted("ch2");
            yield return null;
            Assert.AreEqual(1f, motion.EyeLevel);
            Assert.AreEqual(Unit047Beat.LookAround, motion.Look);

            Quaternion keyBefore = key.localRotation;
            float maxSway = 0f;
            float until = Time.time + 1.7f;
            while (Time.time < until)
            {
                maxSway = Mathf.Max(maxSway, Quaternion.Angle(body.localRotation, Quaternion.identity));
                yield return null;
            }
            Assert.Greater(Quaternion.Angle(key.localRotation, keyBefore), 30f, "The key turns (180 deg/s).");
            Assert.That(maxSway, Is.InRange(0.5f, 2.5f), "A small sway about the waist, never a lurch.");
        }

        [UnityTest]
        public IEnumerator NothingMovesWhileTheStandInIsHidden()
        {
            Unit047Motion motion = Actor(out Transform body, out _, out Transform key);
            yield return Seconds(0.3f);   // no cutscene: the model is hidden
            Assert.AreEqual(Quaternion.identity, key.localRotation);
            Assert.AreEqual(Quaternion.identity, body.localRotation);
        }
    }
}
