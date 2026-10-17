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
using ToyFactory.Runtime.World;

namespace ToyFactory.Tests
{
    /// <summary>Which way a slid box goes: away from the player, and out along a wall when it is jammed.</summary>
    public sealed class BoxShoveTests
    {
        static Func<Vector3, bool> Free(params Vector3[] blocked) =>
            direction =>
            {
                foreach (Vector3 b in blocked)
                {
                    if (b == direction)
                        return false;
                }
                return true;
            };

        static readonly Vector3 East = new Vector3(1f, 0f, 0f);
        static readonly Vector3 West = new Vector3(-1f, 0f, 0f);
        static readonly Vector3 North = new Vector3(0f, 0f, 1f);
        static readonly Vector3 South = new Vector3(0f, 0f, -1f);

        [Test]
        public void OpenFloor_ShovesStraightAwayAlongTheNearestAxis()
        {
            Assert.IsTrue(BoxShove.TryChoose(new Vector3(2f, 0f, 0.4f), Free(), out Vector3 d));
            Assert.AreEqual(East, d);
            Assert.IsTrue(BoxShove.TryChoose(new Vector3(-0.3f, 0f, -2f), Free(), out d));
            Assert.AreEqual(South, d);
        }

        [Test]
        public void AwayIsBlocked_TakesTheFreeWayClosestToIt()
        {
            // The player is east of the box (away is west), and a wall is west of it: along the wall.
            Assert.IsTrue(BoxShove.TryChoose(West, Free(West, South), out Vector3 d));
            Assert.AreEqual(North, d, "out of the corner, north, along the west wall");

            Assert.IsTrue(BoxShove.TryChoose(South, Free(West, South), out d));
            Assert.AreEqual(East, d, "from the north side it slides east along the south wall");
        }

        [Test]
        public void ADiagonalPlayerInFrontOfACorner_StillGetsTheBoxOut()
        {
            // The player is north-east of a box in the south-west corner: away is south-west.
            Assert.IsTrue(BoxShove.TryChoose(new Vector3(-1f, 0f, -1f), Free(West, South), out Vector3 d));
            Assert.IsTrue(d == East || d == North, "one of the two open sides");
        }

        [Test]
        public void NeverShovesStraightIntoThePlayer()
        {
            // Boxed in on three sides with the player on the fourth: nowhere to go.
            Assert.IsFalse(BoxShove.TryChoose(West, Free(West, North, South), out Vector3 d));
            Assert.AreEqual(Vector3.zero, d);
        }

        [Test]
        public void NothingFree_OrNoDirection_ReturnsFalse()
        {
            Assert.IsFalse(BoxShove.TryChoose(East, Free(East, West, North, South), out _));
            Assert.IsFalse(BoxShove.TryChoose(Vector3.zero, Free(), out _));
            Assert.IsFalse(BoxShove.TryChoose(East, null, out _));
        }

        // ---- In the level

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
        public IEnumerator ABoxJammedInACornerComesOutAlongTheWall_AndWalkingIntoABoxMovesNothing()
        {
            AgentSpawner previous = AgentSpawner.Instance;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return Until(() => AgentSpawner.Instance != null && AgentSpawner.Instance != previous
                && ChapterManager.Current != null && ChapterManager.Current.Flow != null && ChapterManager.Current.Flow.HasBegun
                && PlayerState.Current != null && GameObject.Find("Crate_2") != null, 60f, "the level");

            GameClock.Current.RequestState(GameState.Playing);
            yield return Until(() => CutsceneDirector.Current != null && CutsceneDirector.Current.IsPlaying, 10f, "the intro");
            CutsceneDirector.Current.Skip();
            yield return Until(() => GameClock.Current.State == GameState.Playing, 10f, "Playing");
            PropTestLevel.RemoveAgents();   // nothing walks into the crate mid-test

            GameObject crate = GameObject.Find("Crate_2");
            Component box = crate.GetComponent("PushableBox");
            Rigidbody rb = crate.GetComponent<Rigidbody>();
            var interact = box.GetType().GetMethod("Interact");
            var teleport = box.GetType().GetMethod("Teleport");
            Component playerBody = (Component)PlayerState.Current;
            CharacterController controller = playerBody.GetComponent<CharacterController>();

            // A corner of two walls built round the crate at a spot of open floor.
            Vector3 spot = new Vector3(12f, 0.5f, 4f);
            var walls = new GameObject("Test corner");
            int environment = LayerMask.NameToLayer("Environment");
            Vector3 wallWest = new Vector3(spot.x - 0.875f - 1f - 0.03f, 1.5f, spot.z);   // 3 cm off, so nothing is pushed out of it
            Vector3 wallSouth = new Vector3(spot.x, 1.5f, spot.z - 0.875f - 1f - 0.03f);
            foreach (var (centre, size) in new[]
            {
                (wallWest, new Vector3(2f, 3f, 8f)),
                (wallSouth, new Vector3(8f, 3f, 2f))
            })
            {
                var wall = new GameObject("Wall");
                wall.transform.SetParent(walls.transform);
                wall.layer = environment;
                wall.transform.position = centre;
                wall.AddComponent<BoxCollider>().size = size;
            }
            Physics.SyncTransforms();

            teleport.Invoke(box, new object[] { spot, Quaternion.identity });
            yield return PropTestLevel.UntilAtRest(rb, 5f, "the crate to settle in the corner");
            Vector3 start = crate.transform.position;

            // The player is east of it: away is west, into the wall. It must come out north.
            controller.enabled = false;
            controller.transform.position = new Vector3(start.x + 2.5f, 0.05f, start.z);
            controller.enabled = true;
            Physics.SyncTransforms();
            yield return null;
            Vector3 playerAt = playerBody.transform.position;
            interact.Invoke(box, null);
            Vector3 speedAfter = rb.linearVelocity;
            yield return PropTestLevel.UntilGameTime(() => rb.linearVelocity.sqrMagnitude > 0.25f, 2f, "the crate to start sliding");
            yield return PropTestLevel.UntilAtRest(rb, 8f, "the crate to come to rest");
            Assert.Greater(crate.transform.position.z, start.z + 0.3f,
                $"the box slid north along the west wall (start {start}, player {playerAt}, speed right after Interact {speedAfter}, now {crate.transform.position})");
            Assert.Less(rb.linearVelocity.sqrMagnitude, 0.0025f, "and came to rest");
            Assert.AreEqual(start.x, crate.transform.position.x, 0.05f, "it stayed against the west wall");

            // Walking straight into a box does not push it.
            teleport.Invoke(box, new object[] { spot, Quaternion.identity });
            yield return PropTestLevel.UntilAtRest(rb, 5f, "the crate to settle again");
            Vector3 before = crate.transform.position;
            controller.enabled = false;
            controller.transform.position = new Vector3(before.x + 1.6f, 0.05f, before.z);
            controller.enabled = true;
            Physics.SyncTransforms();
            for (int i = 0; i < 60; i++)
            {
                controller.Move(Vector3.left * 4.5f * Time.deltaTime + Vector3.down * 0.01f);
                yield return null;
            }
            Assert.AreEqual(before.x, crate.transform.position.x, 0.05f, "walking into the box did not move it");
            Assert.AreEqual(before.z, crate.transform.position.z, 0.05f);

            UnityEngine.Object.Destroy(walls);
        }
    }
}
