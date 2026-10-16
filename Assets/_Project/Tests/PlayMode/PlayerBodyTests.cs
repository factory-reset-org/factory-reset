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

        // The parts of the body that the camera draws: a part cast as shadow only is not drawn.
        static string DrawnParts(Transform body)
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
            {
                if (r.shadowCastingMode != ShadowCastingMode.ShadowsOnly)
                    names.Add(r.name);
            }
            names.Sort();
            return string.Join(",", names);
        }

        [UnityTest]
        public IEnumerator OnlyTheLegsAndFeetAreDrawn_AheadOfThePlayer_AndOnlyInViewWhenLookingDown()
        {
            yield return LoadLevelAndStart();
            Transform player = ((Component)PlayerState.Current).transform;

            Transform body = player.Find("Body");
            Assert.IsNotNull(body, "the player has Unit 047's body");
            Assert.AreEqual(0, body.GetComponentsInChildren<Collider>(true).Length, "the body cannot touch physics");
            Assert.Greater(body.GetComponentsInChildren<Renderer>(true).Length, 15, "it is the whole model, as a shadow");
            Assert.AreEqual("Foot_L,Foot_R,Leg_L,Leg_R", DrawnParts(body), "only the legs and feet are drawn, not the hips, chest or head");
            Assert.Greater(body.localPosition.z, 0.1f, "the legs stand ahead of the camera, so they come into view when looking down");

            Camera camera = player.GetComponentInChildren<Camera>();
            Transform foot = null;
            foreach (Transform t in body.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Foot_L")
                    foot = t;
            }
            Assert.IsNotNull(foot);

            // The controller sets the camera's pitch from its own field every frame, so that is what is set.
            Component controller = player.GetComponent("PlayerController");
            FieldInfo pitch = controller.GetType().GetField("_pitch", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(pitch, "the controller keeps its pitch in _pitch");

            pitch.SetValue(controller, 10f);
            yield return null;
            yield return null;
            Vector3 view = camera.WorldToViewportPoint(foot.position);
            Assert.IsTrue(view.y < 0f || view.z < 0f, "looking ahead, the feet are below the screen: " + view);

            pitch.SetValue(controller, 70f);
            yield return null;
            yield return null;
            view = camera.WorldToViewportPoint(foot.position);
            Assert.IsTrue(view.z > 0f && view.x > 0f && view.x < 1f && view.y > 0f && view.y < 1f, "looking well down, a foot is on the screen: " + view);
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
