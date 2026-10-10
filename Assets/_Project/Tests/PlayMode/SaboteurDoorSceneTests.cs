using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ToyFactory.AI.Agents.Saboteur;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Cutscenes;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.World;

namespace ToyFactory.Tests
{
    /// <summary>
    /// A Saboteur closes a door in the real scenes: the brain picks the door, walks to it and asks, the
    /// controller carries the request out through the door registry, and the answer comes back to the
    /// brain. Bootstrap loads Env, Interactables, Agents and UI.
    /// </summary>
    /// <remarks>
    /// Every door in the level is the only link between its two rooms, so closing one would lock the
    /// player out and the Saboteur never offers it (a lockout is rejected by design). The test therefore
    /// cuts a second gap through the wall next to door 1, which gives the player a longer way round, as a
    /// level with a loop would. Door 1's cells are x = 41, y = 18 to 23, in a wall three cells thick;
    /// the gap is at y = 30 and 31. Saboteur A starts beside the door.
    /// </remarks>
    public sealed class SaboteurDoorSceneTests
    {
        const float LoadTimeout = 60f;
        const int DoorId = 1;
        static readonly Vector2Int DoorCell = new Vector2Int(41, 18);

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;

            Scene empty = SceneManager.CreateScene("Empty after door " + Guid.NewGuid());
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

        static IEnumerator Until(Func<bool> condition, float timeout, string what)
        {
            float until = Time.realtimeSinceStartup + timeout;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > until)
                    Assert.Fail("Timed out waiting for " + what);
                yield return null;
            }
        }

        static void Teleport(Component body, Vector3 to)
        {
            var controller = body.GetComponent<CharacterController>();
            if (controller != null)
                controller.enabled = false;
            body.transform.position = to;
            if (controller != null)
                controller.enabled = true;
        }

        [UnityTest]
        public IEnumerator SaboteurClosesTheDoorThatLengthensThePlayersRoute()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return Until(() => AgentSpawner.Instance != null && AgentSpawner.Instance.SpawnedAgents.Count == 7
                && CutsceneDirector.Current != null && PlayerState.Current != null && GridManager.Current != null,
                LoadTimeout, "Bootstrap to spawn every agent");
            yield return Until(() => CutsceneDirector.Current.IsPlaying, 10f, "the intro");
            CutsceneDirector.Current.Skip();
            yield return Until(() => GameClock.Current != null && GameClock.Current.State == GameState.Playing, 10f, "Playing");

            GridGraph grid = GridManager.Current;
            Assert.IsFalse(grid.GetNode(DoorCell).IsDoorClosed, "Door 1 starts open.");
            Assert.AreEqual(DoorId, grid.GetNode(DoorCell).DoorId);

            // A second way through the wall, so closing door 1 lengthens the route instead of cutting it.
            using (GridGraph.Batch batch = grid.BeginBatch())
            {
                for (int x = 40; x <= 42; x++)
                {
                    batch.SetWalkable(new Vector2Int(x, 30), true);
                    batch.SetWalkable(new Vector2Int(x, 31), true);
                }
                batch.Commit();
            }

            // The player on one side of the door and the objective on the other.
            var playerCell = new Vector2Int(24, 18);
            var objectiveCell = new Vector2Int(52, 18);
            AgentSpawner.Instance.Blackboard.SetObjectiveTargets(new[] { new ObjectiveTarget(9999, objectiveCell, ObjectiveTargetKind.Task) });
            Teleport((Component)PlayerState.Current, grid.CellToWorld(playerCell));

            AgentController[] saboteurs = AgentSpawner.Instance.SpawnedAgents.Where(a => a.Type == AgentType.Saboteur).ToArray();
            Assert.AreEqual(4, saboteurs.Length);

            // Saboteur A starts a few metres from the door, on clear floor, rather than walking across the
            // level: the test is about the door chain, not the patrol routes.
            AgentController closer = saboteurs.First(a => a.Identity.SquadLetter == 'A');
            Teleport(closer, grid.CellToWorld(new Vector2Int(36, 26)));
            bool sawCloseDoor = false;
            float until = Time.realtimeSinceStartup + 40f;
            while (!grid.GetNode(DoorCell).IsDoorClosed && Time.realtimeSinceStartup < until)
            {
                sawCloseDoor |= saboteurs.Any(a => a.DebugState == "CloseDoor");
                yield return null;
            }

            Assert.IsTrue(grid.GetNode(DoorCell).IsDoorClosed, "A Saboteur closed door 1.");
            Assert.IsTrue(sawCloseDoor, "A Saboteur was in the CloseDoor state on the way.");

            // The squad does not close it twice, and the claim is let go once the door shut.
            yield return new WaitForSeconds(1f);
            Assert.IsNull(AgentSpawner.Instance.Blackboard.Claims.ClaimedBy(DoorId), "The door's claim is released.");
            Assert.IsTrue(grid.GetNode(DoorCell).IsDoorClosed);
        }
    }
}
