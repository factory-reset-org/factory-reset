using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Journey.Cutscenes;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Tests
{
    /// <summary>
    /// Pressing Interact on a pushable box shoves it away from the player and it glides to a stop on
    /// the floor, in the real level. The box type is in the default assembly, so it is reached by name.
    /// </summary>
    public sealed class PushableBoxSlideTests
    {
        // Leaves nothing behind: the tests that run next load Bootstrap themselves and must not
        // find this level's spawner, clock or player still alive.
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

        [UnityTest]
        public IEnumerator InteractOnABox_ShovesItAwayFromThePlayer_ItGlidesAndSettlesOnTheFloor()
        {
            AgentSpawner previous = AgentSpawner.Instance;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return Until(() => AgentSpawner.Instance != null && AgentSpawner.Instance != previous
                && ChapterManager.Current != null && ChapterManager.Current.Flow != null && ChapterManager.Current.Flow.HasBegun
                && PlayerState.Current != null && GameObject.Find("Crate_2") != null, 60f, "the level");

            // Bootstrap waits on the title screen. Start plays the intro, so it is skipped too: the tests
            // that run after this one in the same level expect the clock in Playing, not in a cutscene.
            if (GameClock.Current != null)
                GameClock.Current.RequestState(GameState.Playing);
            yield return Until(() => CutsceneDirector.Current != null && CutsceneDirector.Current.IsPlaying, 10f, "the intro");
            CutsceneDirector.Current.Skip();
            yield return Until(() => GameClock.Current.State == GameState.Playing, 10f, "Playing");

            GameObject crate = GameObject.Find("Crate_2");
            Component box = crate.GetComponent("PushableBox");
            Assert.IsNotNull(box, "Crate_2 has a PushableBox");
            Rigidbody rb = crate.GetComponent<Rigidbody>();
            Collider col = crate.GetComponent<Collider>();

            // The player stands west of the crate. The crate rests where the level put it.
            yield return Until(() => rb.linearVelocity.sqrMagnitude < 0.0001f, 5f, "the crate to rest");
            Vector3 start = crate.transform.position;
            var playerBody = PlayerState.Current as Component;
            var controller = playerBody != null ? playerBody.GetComponent<CharacterController>() : null;
            Assert.IsNotNull(controller, "the player has a CharacterController");
            controller.enabled = false;
            controller.transform.position = new Vector3(start.x - 1.5f, 0.05f, start.z);
            controller.enabled = true;
            Physics.SyncTransforms();
            yield return null;

            box.GetType().GetMethod("Interact").Invoke(box, null);
            yield return Until(() => rb.linearVelocity.x > 1f, 1f, "the box to start sliding away from the player");

            float lowest = float.MaxValue;
            float until = Time.realtimeSinceStartup + 6f;
            while (rb.linearVelocity.sqrMagnitude > 0.0025f && Time.realtimeSinceStartup < until)
            {
                lowest = Mathf.Min(lowest, col.bounds.min.y);
                yield return null;
            }

            float glide = crate.transform.position.x - start.x;
            Assert.Greater(glide, 0.4f, "a shove carries the box a good way: about half a metre or more");
            Assert.Less(glide, 2.5f, "and it does not run away: friction stops it");
            Assert.AreEqual(start.z, crate.transform.position.z, 0.05f, "it slid straight away from the player");
            Assert.Greater(lowest, -0.05f, "it stayed on the floor while it slid");
            Assert.AreEqual(start.y, crate.transform.position.y, 0.05f, "and rests at the height it started");
        }
    }
}
