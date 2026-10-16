using System;
using System.Collections;
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
    /// The player's own legs, the hand that slides a box, and boxes that stay upright, in the real
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
        public IEnumerator ThePlayerHasLegsThatTheCameraSeesAndNothingAboveThem()
        {
            yield return LoadLevelAndStart();
            Transform player = ((Component)PlayerState.Current).transform;

            Transform body = player.Find("Body (own legs)");
            Assert.IsNotNull(body, "the player stands in Unit 047's body");
            Assert.AreEqual(0, body.GetComponentsInChildren<Collider>(true).Length, "the body cannot touch physics");

            int legs = 0, hidden = 0;
            foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
            {
                bool lower = r.name.StartsWith("Leg_") || r.name.StartsWith("Foot_") || r.name == "Pelvis";
                if (lower)
                {
                    legs++;
                    Assert.AreEqual(ShadowCastingMode.On, r.shadowCastingMode, r.name + " is drawn for the camera");
                }
                else
                {
                    hidden++;
                    Assert.AreEqual(ShadowCastingMode.ShadowsOnly, r.shadowCastingMode, r.name + " is not drawn for the camera");
                }
            }

            Assert.AreEqual(5, legs, "two legs, two feet and the pelvis");
            Assert.Greater(hidden, 10, "the rest of the model is only a shadow");
        }

        [UnityTest]
        public IEnumerator TheHandReachesOutCallsBackOnArrivalAndGoesAway()
        {
            yield return LoadLevelAndStart();
            Component player = (Component)PlayerState.Current;
            Type bodyType = Type.GetType("ToyFactory.Player.PlayerBody, Assembly-CSharp");
            Component body = player.GetComponent(bodyType);
            Assert.IsNotNull(body, "the player has a PlayerBody");

            int arrived = 0;
            object accepted = bodyType.GetMethod("Thrust").Invoke(body, new object[] { (Action)(() => arrived++) });
            Assert.IsTrue((bool)accepted);
            var isReaching = bodyType.GetProperty("IsReaching");
            Assert.IsTrue((bool)isReaching.GetValue(body), "the hand is out");
            Assert.IsFalse((bool)bodyType.GetMethod("Thrust").Invoke(body, new object[] { (Action)(() => arrived += 100) }),
                "a second reach is refused while the first is under way");

            yield return Until(() => arrived > 0, 2f, "the hand to arrive");
            yield return Until(() => !(bool)isReaching.GetValue(body), 2f, "the hand to go back");
            Assert.AreEqual(1, arrived, "the callback ran once");
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
