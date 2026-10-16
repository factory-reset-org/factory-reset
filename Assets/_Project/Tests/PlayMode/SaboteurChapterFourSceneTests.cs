using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Journey.Debugging;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.World;

namespace ToyFactory.Tests
{
    /// <summary>
    /// CloseDoor on the real map, with no wall cut and no door forced. Doors 3 and 4 (the Control Room's
    /// two entrances) are locked until the Chapter 3 cutscene, so before Chapter 4 every door is the only
    /// link between its rooms and a closure would be a lockout. In Chapter 4 the four rooms form a ring,
    /// so a closed door only makes the player walk the long way round, and the Saboteur may close it.
    /// The rooms: Assembly south-west, Painting south-east, Storage north-east, Control north-west.
    /// Door 1 joins Assembly and Painting, door 2 Painting and Storage, door 3 Storage and Control, door 4
    /// Control and Assembly. Only the Saboteur under test (and one more in the second test) stays alive, so
    /// nothing else shoots at the player or closes a door.
    /// </summary>
    public sealed class SaboteurChapterFourSceneTests
    {
        const float LoadTimeout = 60f;
        static readonly Vector2Int ControlGoal = new Vector2Int(16, 56);

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            Scene empty = SceneManager.CreateScene("Empty after Chapter 4 doors " + Guid.NewGuid());
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

        // The walkable cell nearest to a wanted one.
        static Vector2Int Walkable(GridGraph grid, Vector2Int wanted)
        {
            Assert.IsTrue(grid.TryFindNearestTraversable(wanted, 10, out Vector2Int cell), $"Floor near {wanted}.");
            return cell;
        }

        static void Place(GridGraph grid, Component body, Vector2Int cell)
        {
            Vector3 at = grid.CellToWorld(Walkable(grid, cell));
            Teleport(body, new Vector3(at.x, body.transform.position.y, at.z));
        }

        // Moves a Saboteur the way a knock-down and a reboot would: it is stunned (the brain drops any plan it
        // had, including a route it sent from where it used to be) and put down in the new place, then reboots
        // and decides afresh from there. Teleporting one mid-plan would leave it following a stale route.
        static void Relocate(GridGraph grid, AgentController saboteur, Vector2Int cell)
        {
            saboteur.Disable(0.2f);
            Place(grid, saboteur, cell);
        }

        static Vector2Int DoorCell(GridGraph grid, int doorId)
        {
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                GridNode node = grid.GetNode(new Vector2Int(x, y));
                if (node.DoorId == doorId)
                    return node.Cell;
            }

            Assert.Fail("No door " + doorId);
            return default;
        }

        // Chapter 4 with only the named Saboteurs alive.
        static IEnumerator LoadChapterFour(params char[] keep)
        {
            SceneManager.LoadScene("Bootstrap");
            yield return Until(() => ChapterManager.Current != null && ChapterManager.Current.Flow != null
                && ChapterManager.Current.Flow.HasBegun && UnityEngine.Object.FindAnyObjectByType<ChapterJump>() != null
                && AgentSpawner.Instance != null && AgentSpawner.Instance.SpawnedAgents.Count == 7
                && GridManager.Current != null && PlayerState.Current != null, LoadTimeout, "the level");
            if (GameClock.Current != null)
                GameClock.Current.RequestState(GameState.Playing);
            yield return null;
            ChapterJump jump = UnityEngine.Object.FindAnyObjectByType<ChapterJump>();
            jump.JumpTo(4);
            yield return Until(() => !jump.IsJumping, 20f, "the jump to Chapter 4");
            yield return Until(() => GameClock.Current != null && GameClock.Current.State == GameState.Playing, 20f, "Playing");

            foreach (AgentController agent in AgentSpawner.Instance.SpawnedAgents.ToArray())
            {
                bool wanted = agent.Type == AgentType.Saboteur && keep.Contains(agent.Identity.SquadLetter);
                if (!wanted)
                    agent.Scrap();
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator InChapterFourASaboteurClosesTheDoorOnThePlayersRouteWithNoWallCutAndNothingForced()
        {
            yield return LoadChapterFour('A');
            GridGraph grid = GridManager.Current;
            WorldBlackboard board = AgentSpawner.Instance.Blackboard;
            Vector2Int door4 = DoorCell(grid, 4);
            Assert.IsFalse(grid.GetNode(door4).IsDoorClosed, "Door 4 is open in Chapter 4.");

            // The player is deep in Assembly and wants the Control Room, so the short way is through door 4.
            board.SetObjectiveTargets(new[] { new ObjectiveTarget(9999, ControlGoal, ObjectiveTargetKind.Task) });
            var player = (Component)PlayerState.Current;
            Place(grid, player, new Vector2Int(22, 6));
            AgentController closer = AgentSpawner.Instance.SpawnedAgents.First(a => a.Identity.SquadLetter == 'A');
            Relocate(grid, closer, new Vector2Int(22, 36));
            yield return new WaitForSeconds(0.5f);

            bool sawCloseDoor = false;
            float until = Time.realtimeSinceStartup + 40f;
            while (!grid.GetNode(door4).IsDoorClosed && Time.realtimeSinceStartup < until)
            {
                sawCloseDoor |= closer.DebugState == "CloseDoor";
                yield return null;
            }

            PlayerSnapshot seen = board.Player;
            Assert.IsTrue(grid.GetNode(door4).IsDoorClosed,
                "Saboteur A closed door 4, the player's short way to the Control Room. "
                + $"A is {closer.DebugState} at {grid.WorldToCell(closer.transform.position)} dead {closer.IsDead} disabled {closer.IsDisabled} frozen {closer.IsFrozen}; "
                + $"player known {seen.IsKnown} alive {seen.IsAlive} cell {seen.Cell}; objectives {string.Join(",", board.ObjectiveTargets.Select(t => t.Cell.ToString()))}; "
                + $"claim on door 4 {board.Claims.ClaimedBy(4)}; doors closed {string.Join(",", new[] { 1, 2, 3, 4 }.Where(d => grid.GetNode(DoorCell(grid, d)).IsDoorClosed))}.");
            Assert.IsTrue(sawCloseDoor, "It was in the CloseDoor state on the way.");

            // The player is not locked out: the long way round (doors 1, 2, 3) is still open.
            var pathfinder = new AStarSearch(grid);
            Vector2Int from = grid.WorldToCell(player.transform.position);
            PathResult route = pathfinder.FindPath(Walkable(grid, from), Walkable(grid, ControlGoal), BaseCostModel.Instance);
            Assert.IsTrue(route.Found, "There is still a way to the Control Room.");

            // And the squad does not go on to shut the long way round: with door 4 closed, doors 1 to 3 are the
            // only way, so each would be a lockout.
            yield return new WaitForSeconds(6f);
            foreach (int id in new[] { 1, 2, 3 })
                Assert.IsFalse(grid.GetNode(DoorCell(grid, id)).IsDoorClosed, $"Door {id} stays open.");
        }

        [UnityTest]
        public IEnumerator TwoSaboteursNeverSealTheControlRoomsTwoEntrances()
        {
            yield return LoadChapterFour('A', 'B');
            GridGraph grid = GridManager.Current;
            WorldBlackboard board = AgentSpawner.Instance.Blackboard;
            Vector2Int door3 = DoorCell(grid, 3);
            Vector2Int door4 = DoorCell(grid, 4);

            // The player is in Storage and wants the Control Room: door 3 is the short way, door 4 the long one.
            board.SetObjectiveTargets(new[] { new ObjectiveTarget(9999, ControlGoal, ObjectiveTargetKind.Task) });
            var player = (Component)PlayerState.Current;
            Place(grid, player, new Vector2Int(60, 66));
            AgentController a = AgentSpawner.Instance.SpawnedAgents.First(x => x.Identity.SquadLetter == 'A');
            AgentController b = AgentSpawner.Instance.SpawnedAgents.First(x => x.Identity.SquadLetter == 'B');
            Relocate(grid, a, new Vector2Int(44, 64));   // beside door 3, on the Storage side
            Relocate(grid, b, new Vector2Int(24, 36));   // beside door 4, on the Assembly side
            yield return new WaitForSeconds(0.5f);

            float until = Time.realtimeSinceStartup + 40f;
            while (!grid.GetNode(door3).IsDoorClosed && Time.realtimeSinceStartup < until)
                yield return null;
            Assert.IsTrue(grid.GetNode(door3).IsDoorClosed, "Door 3, the player's short way, was closed.");

            // Door 4 is now the only way into the Control Room, so closing it would lock the player out.
            float watchUntil = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < watchUntil)
            {
                Assert.IsFalse(grid.GetNode(door4).IsDoorClosed, "Door 4 must stay open: both entrances shut would seal the room.");
                yield return null;
            }

            var pathfinder = new AStarSearch(grid);
            Vector2Int from = Walkable(grid, grid.WorldToCell(player.transform.position));
            Assert.IsTrue(pathfinder.FindPath(from, Walkable(grid, ControlGoal), BaseCostModel.Instance).Found,
                "The player can still reach the Control Room.");
        }
    }
}
