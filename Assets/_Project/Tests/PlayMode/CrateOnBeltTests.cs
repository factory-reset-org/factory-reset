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
    /// The 1.75 m crate, with a collider as big as its model, still rides the top belt to the stop and
    /// latches the pressure plate: nothing at the end of the belt catches its edge. The prop types are
    /// in the default assembly, so they are reached by name.
    /// </summary>
    public sealed class CrateOnBeltTests
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

        [UnityTest]
        public IEnumerator ABigCrateOnTheTopBeltRidesToTheStopAndLatchesThePlate()
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

            Type leverType = Type.GetType("ToyFactory.Interaction.ConveyorLever, Assembly-CSharp");
            Type plateType = Type.GetType("ToyFactory.Interaction.PressurePlate, Assembly-CSharp");
            Component lever = UnityEngine.Object.FindAnyObjectByType(leverType) as Component;
            Component plate = UnityEngine.Object.FindAnyObjectByType(plateType) as Component;
            Assert.IsNotNull(lever, "the conveyor lever");
            Assert.IsNotNull(plate, "the pressure plate");

            leverType.GetMethod("Interact").Invoke(lever, null);

            GameObject crate = GameObject.Find("Crate_1");
            Component box = crate.GetComponent("PushableBox");
            Assert.AreEqual(1.75f, crate.GetComponent<BoxCollider>().size.x, 0.001f, "the crate's collider is as big as its model");
            box.GetType().GetMethod("Teleport").Invoke(box, new object[] { new Vector3(10f, 0.5f, 17.5f), Quaternion.identity });

            var completed = plateType.GetProperty("IsCompleted");
            yield return Until(() => (bool)completed.GetValue(plate), 20f, "the crate to reach the plate and latch it");
            Assert.Greater(crate.transform.position.x, 14.5f, "it rode the belt to the end");
        }
    }
}
