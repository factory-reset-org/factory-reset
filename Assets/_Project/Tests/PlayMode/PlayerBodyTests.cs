using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Journey.Cutscenes;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Tests
{
    /// <summary>
    /// The player's shadow body, never drawn for the camera, and boxes that stay upright, in the real
    /// level. The body and box types are in the default assembly, so they are reached by name.
    /// </summary>
    public sealed class PlayerBodyTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            Scene empty = SceneManager.CreateScene("Empty after " + Guid.NewGuid());
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != empty && scene.isLoaded)
                    yield return SceneManager.UnloadSceneAsync(scene);
            }

            if (GameClock.Current is UnityEngine.Object clock && clock == null)
                GameClock.Publish(null);
            if (PlayerState.Current is UnityEngine.Object player && player == null)
                PlayerState.Publish(null);
        }

        static IEnumerator Until(Func<bool> condition, float seconds, string what)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > until)
                    Assert.Fail("Timed out waiting for " + what);
                yield return null;
            }
        }

        static IEnumerator LoadLevelAndStart()
        {
            AgentSpawner previous = AgentSpawner.Instance;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return Until(() => AgentSpawner.Instance != null && AgentSpawner.Instance != previous
                && ChapterManager.Current != null && ChapterManager.Current.Flow != null && ChapterManager.Current.Flow.HasBegun
                && PlayerState.Current != null, 60f, "the level");

            GameClock.Current.RequestState(GameState.Playing);
            yield return Until(() => CutsceneDirector.Current != null && CutsceneDirector.Current.IsPlaying, 10f, "the intro");
            CutsceneDirector.Current.Skip();
            yield return Until(() => GameClock.Current.State == GameState.Playing, 10f, "Playing");
        }

        // Counts the parts of the body that the camera draws: a part cast as shadow only is not drawn.
        static int Drawn(Transform body)
        {
            int drawn = 0;
            foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
            {
                if (r.shadowCastingMode != ShadowCastingMode.ShadowsOnly)
                    drawn++;
            }
            return drawn;
        }

        [UnityTest]
        public IEnumerator TheBodyOnlyCastsAShadow_NoPartOfItIsEverDrawnForTheCamera()
        {
            yield return LoadLevelAndStart();
            Transform player = ((Component)PlayerState.Current).transform;

            Transform body = player.Find("Body (shadow)");
            Assert.IsNotNull(body, "the player has Unit 047's body for a shadow");
            Assert.AreEqual(0, body.GetComponentsInChildren<Collider>(true).Length, "the body cannot touch physics");
            Assert.Greater(body.GetComponentsInChildren<Renderer>(true).Length, 15, "it is the whole model");

            // The controller sets the camera's pitch from its own field every frame, so that is what is set.
            Component controller = player.GetComponent("PlayerController");
            Assert.IsNotNull(controller, "the player has a PlayerController");
            FieldInfo pitch = controller.GetType().GetField("_pitch", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(pitch, "the controller keeps its pitch in _pitch");

            foreach (float angle in new[] { 0f, 40f, 65f, 80f })
            {
                pitch.SetValue(controller, angle);
                yield return null;
                yield return null;
                Assert.AreEqual(0, Drawn(body), "no part of the body is drawn when looking " + angle + " degrees down");
            }
        }

        [UnityTest]
        public IEnumerator PushableBoxesStayUprightSoTheyCannotTipIntoTheFloor()
        {
            yield return LoadLevelAndStart();
            foreach (string name in new[] { "Crate_1", "Crate_2", "Crate_3" })
            {
                GameObject crate = GameObject.Find(name);
                Assert.IsNotNull(crate, name);
                RigidbodyConstraints c = crate.GetComponent<Rigidbody>().constraints;
                Assert.IsTrue((c & RigidbodyConstraints.FreezeRotation) == RigidbodyConstraints.FreezeRotation,
                    name + " cannot rotate");
            }
        }
    }
}
