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

        [UnityTest]
        public IEnumerator TheBodyIsOnlyAShadow_AndTheArmsHoldTheGunOnTheViewModelLayer()
        {
            yield return LoadLevelAndStart();
            Transform player = ((Component)PlayerState.Current).transform;

            // The shadow body: the whole model, never drawn for the camera.
            Transform body = player.Find("Body");
            Assert.IsNotNull(body, "the player has Unit 047's body for a shadow");
            Assert.AreEqual(0, body.GetComponentsInChildren<Collider>(true).Length, "the body cannot touch physics");
            Assert.Greater(body.GetComponentsInChildren<Renderer>(true).Length, 15, "it is the whole model");
            foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
                Assert.AreEqual(ShadowCastingMode.ShadowsOnly, r.shadowCastingMode, r.name + " casts a shadow and is not drawn");

            // The arms: a copy under the gun, on the overlay camera's layer, only the arms and hands drawn.
            Transform arms = player.Find("CameraPivot/Camera/BlasterViewModel/Model/Arms");
            Assert.IsNotNull(arms, "the player's arms are under the first-person gun");
            Assert.AreEqual(0, arms.GetComponentsInChildren<Collider>(true).Length, "the arms cannot touch physics");
            Assert.AreEqual(0, arms.GetComponentsInChildren<MonoBehaviour>(true).Length, "the model's own motion scripts are not on the arms");

            int viewLayer = LayerMask.NameToLayer("ViewModel");
            var drawn = new System.Collections.Generic.List<string>();
            foreach (Renderer r in arms.GetComponentsInChildren<Renderer>(true))
            {
                Assert.AreEqual(viewLayer, r.gameObject.layer, r.name + " is on the ViewModel layer");
                if (r.enabled)
                {
                    drawn.Add(r.name);
                    Assert.IsFalse(r.forceRenderingOff, r.name + " is shown while playing, with the gun");
                }
            }

            drawn.Sort();
            Assert.AreEqual("Arm_L,Arm_R,Hand_L,Hand_R,ShoulderBall_L,ShoulderBall_R", string.Join(",", drawn),
                "only the arms, hands and shoulders are drawn, not the model's head, chest, legs or its own gun");

            // The arms are laid over the gun: the model's gun, hidden, sits where the first-person gun is.
            Renderer gun = player.Find("CameraPivot/Camera/BlasterViewModel/Model/BlasterModel").GetComponent<Renderer>();
            Bounds modelGun = default;
            bool first = true;
            foreach (Renderer r in arms.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name != "Blaster_Body" && r.name != "Blaster_Barrel")
                    continue;
                if (first) { modelGun = r.bounds; first = false; }
                else modelGun.Encapsulate(r.bounds);
            }
            Assert.IsFalse(first, "the model has its own gun parts to line up with");
            Assert.Less(Vector3.Distance(modelGun.center, gun.bounds.center), 0.03f, "the model's gun sits on the first-person gun");
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
