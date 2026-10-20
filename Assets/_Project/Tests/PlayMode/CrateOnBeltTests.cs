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
            PropTestLevel.RemoveAgents();   // nothing walks into the crate mid-test
        }

        [UnityTest]
        public IEnumerator ABoxSlidFromTheFloorClimbsTheBeltEdgeAndIsCarried()
        {
            yield return LoadLevelAndStart();

            Type leverType = Type.GetType("ToyFactory.Interaction.ConveyorLever, Assembly-CSharp");
            leverType.GetMethod("Interact").Invoke(UnityEngine.Object.FindAnyObjectByType(leverType), null);

            GameObject crate = GameObject.Find("Crate_1");
            Component box = crate.GetComponent("PushableBox");
            Rigidbody rb = crate.GetComponent<Rigidbody>();
            // On the floor just south of the top belt (z 16.4 to 18.6, its edge slope from about 16.2), with
            // the player south of it.
            box.GetType().GetMethod("Teleport").Invoke(box, new object[] { new Vector3(6f, 0.5f, 15.15f), Quaternion.identity });
            yield return PropTestLevel.UntilAtRest(rb, 5f, "the crate to settle on the floor");
            Assert.AreEqual(0.5f, crate.transform.position.y, 0.03f, "it starts on the floor, not on the belt");

            Component player = (Component)PlayerState.Current;
            CharacterController controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            controller.transform.position = new Vector3(6f, 0.05f, 12.5f);
            controller.enabled = true;
            Physics.SyncTransforms();
            yield return null;

            box.GetType().GetMethod("Interact").Invoke(box, null);
            yield return PropTestLevel.UntilGameTime(() => crate.transform.position.z > 16.5f && crate.transform.position.y > 0.52f
                && crate.transform.position.x > 7f, 8f, "the crate to climb onto the belt and be carried");

            Assert.Greater(crate.transform.position.z, 16.5f, "it went over the belt's edge");
            Assert.Greater(crate.transform.position.y, 0.52f, "and is standing on the belt, not beside it");
            Assert.Greater(crate.transform.position.x, 7f, "and the belt carried it along");
        }

        [UnityTest]
        public IEnumerator ThePaintingRoomCratesAreTheBigPushableCrate()
        {
            yield return LoadLevelAndStart();
            Vector3[] placed = new Vector3[3];
            for (int i = 0; i < 3; i++)
            {
                GameObject crate = GameObject.Find("PaintCrate_" + (i + 1));
                Assert.IsNotNull(crate, "PaintCrate_" + (i + 1));
                placed[i] = crate.transform.position;
                Assert.AreEqual(1.75f, crate.GetComponent<BoxCollider>().size.x, 0.001f, crate.name + " is the full-size crate");
                Transform model = crate.transform.Find("Model");
                Assert.IsNotNull(model, crate.name + " has its model");
                Assert.IsTrue(model.name == "Model" && model.GetComponentInChildren<MeshRenderer>() != null);
            }

            yield return new WaitForSeconds(1.5f);
            for (int i = 0; i < 3; i++)
            {
                GameObject crate = GameObject.Find("PaintCrate_" + (i + 1));
                Assert.Less(Vector3.Distance(placed[i], crate.transform.position), 0.15f,
                    crate.name + " sat where the level put it: it was not shoved out of anything it overlaps");
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
            PropTestLevel.RemoveAgents();

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
            yield return PropTestLevel.UntilGameTime(() => (bool)completed.GetValue(plate), 30f, "the crate to reach the plate and latch it");
            Assert.Greater(crate.transform.position.x, 14.5f, "it rode the belt to the end");
        }
    }
}
