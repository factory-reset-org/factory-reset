using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ToyFactory.AI.Agents.Saboteur;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Cutscenes;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.World;
using ToyFactory.UI;

namespace ToyFactory.Tests
{
    /// <summary>
    /// The Saboteur's edge cases in the real level (plan C3): a claim holder scrapped mid-action, the whole
    /// squad scrapped, and a cutscene that starts in the middle of an attack.
    /// </summary>
    public sealed class SaboteurEdgeSceneTests
    {
        const float LoadTimeout = 60f;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;

            // Leave an empty scene behind, so the game does not run under the next test.
            Scene empty = SceneManager.CreateScene("Empty after Saboteur edge case " + Guid.NewGuid());
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

        static IEnumerator LoadAndStartPlaying()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return Until(() => AgentSpawner.Instance != null && AgentSpawner.Instance.SpawnedAgents.Count == 7
                && CutsceneDirector.Current != null && PlayerState.Current != null
                && GridManager.Current != null && AgentSpawner.Instance.Blackboard.SabotageFacts.Count > 0,
                LoadTimeout, "Bootstrap to spawn every agent and list the facts");
            if (GameClock.Current != null)
                GameClock.Current.RequestState(GameState.Playing);
            yield return Until(() => CutsceneDirector.Current.IsPlaying, 10f, "the intro");
            CutsceneDirector.Current.Skip();
            yield return Until(() => GameClock.Current != null && GameClock.Current.State == GameState.Playing, 10f, "Playing");
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

        static AgentController Saboteur(char letter) => AgentSpawner.Instance.SpawnedAgents
            .First(a => a.Type == AgentType.Saboteur && a.Identity.SquadLetter == letter);

        // A walkable cell 8 to 14 cells (4 to 7 m) from the target cell.
        static bool TryCellNear(GridGraph grid, Vector2Int target, out Vector2Int cell)
        {
            for (int radius = 8; radius <= 14; radius++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    foreach (int dy in new[] { -radius, radius })
                    {
                        cell = new Vector2Int(target.x + dx, target.y + dy);
                        if (grid.IsTraversable(cell))
                            return true;
                    }
                }
            }

            cell = default;
            return false;
        }

        static bool OnAWalkableCell(Vector3 position)
        {
            GridGraph grid = GridManager.Current;
            return grid.TryWorldToCell(position, out Vector2Int cell) && grid.IsTraversable(cell);
        }

        [UnityTest]
        public IEnumerator SaboteurAScrappedOnItsWayToABatteryFreesTheClaimAndDropsTheKeycardOnWalkableGround()
        {
            yield return LoadAndStartPlaying();
            WorldBlackboard board = AgentSpawner.Instance.Blackboard;
            GridGraph grid = GridManager.Current;
            AgentController saboteur = Saboteur('A');
            var player = (Component)PlayerState.Current;
            string factsAtStart = string.Join(",", board.SabotageFacts.Select(f => $"{f.Kind}{f.Id}{(f.Available ? "" : "x")}"));

            // With the player short of ammunition the whole squad goes for the batteries. B to D are scrapped
            // first (a partly destroyed squad), so Saboteur A is the only one that can claim the one it is put near.
            foreach (char letter in new[] { 'B', 'C', 'D' })
                Saboteur(letter).Scrap();

            // The player is short of ammunition, so a battery is worth taking.
            Component battery = player.GetComponent("PlayerBattery");
            Assert.IsNotNull(battery, "The player carries a PlayerBattery.");
            var spend = battery.GetType().GetMethod("TrySpendShot");
            for (int i = 0; i < 14; i++)
                spend.Invoke(battery, null);

            Vector3 playerAt = player.transform.position;
            SabotageFact target = board.SabotageFacts
                .Where(f => f.Kind == SabotageFactKind.Battery)
                .OrderByDescending(f => Vector2.Distance(new Vector2(playerAt.x, playerAt.z),
                    new Vector2(grid.CellToWorld(f.Cell).x, grid.CellToWorld(f.Cell).z)))
                .First();
            Assert.IsTrue(SabotageTargetCells.TrySnap(grid, target.Cell, out Vector2Int batteryCell));
            Assert.IsTrue(TryCellNear(grid, batteryCell, out Vector2Int start));
            Vector3 at = grid.CellToWorld(start);
            Teleport(saboteur, new Vector3(at.x, saboteur.transform.position.y, at.z));

            var spawned = new List<ITask>();
            Action<ITask> onSpawned = spawned.Add;
            TaskEvents.OnTaskSpawned += onSpawned;
            try
            {
                // It sets off for the battery and holds the claim on it.
                float claimUntil = Time.time + 10f;
                while (board.Claims.ClaimedBy(target.Id) != saboteur.Identity.Id && Time.time < claimUntil)
                    yield return null;
                PlayerSnapshot seen = board.Player;
                string factsNow = string.Join(",", board.SabotageFacts.Select(f => $"{f.Kind}{f.Id}{(f.Available ? "" : "x")}"));
                Assert.AreEqual(saboteur.Identity.Id, board.Claims.ClaimedBy(target.Id),
                    $"Saboteur A should claim battery {target.Id}; it is in state {saboteur.DebugState}, ammo {PlayerState.Current.AmmoFraction}. "
                    + $"Facts at start {factsAtStart}, now {factsNow}, "
                    + $"saboteur cell {grid.WorldToCell(saboteur.transform.position)} (started {start}), battery cell {batteryCell}, "
                    + $"board player known {seen.IsKnown} alive {seen.IsAlive} cell {seen.Cell} sprint {seen.SprintSpeed} ammo {seen.AmmoFraction}, "
                    + $"alive {saboteur.IsDead}/{saboteur.IsDisabled}/{saboteur.IsFrozen}, brain {saboteur.Brain?.GetType().Name}.");

                saboteur.Scrap();
                yield return null;

                Assert.AreNotEqual(saboteur.Identity.Id, board.Claims.ClaimedBy(target.Id), "The claim is released at once.");
                Assert.IsTrue(saboteur.IsDead);
                Assert.AreEqual(1, spawned.Count, "The keycard task is announced once.");
                Assert.IsTrue(OnAWalkableCell(((Component)spawned[0]).transform.position), "The keycard lands where the player can walk.");

                // Nothing tries to go on with the scrapped Saboteur's plan, and nothing is announced twice.
                saboteur.Scrap();
                yield return new WaitForSeconds(1f);
                Assert.AreEqual(1, spawned.Count);
            }
            finally
            {
                TaskEvents.OnTaskSpawned -= onSpawned;
            }
        }

        [UnityTest]
        public IEnumerator AllFourSaboteursScrappedReleasesEveryClaimAwardsTheBonusOnceAndTheRestCarryOn()
        {
            yield return LoadAndStartPlaying();
            WorldBlackboard board = AgentSpawner.Instance.Blackboard;
            AgentController[] squad = new[] { 'A', 'B', 'C', 'D' }.Select(Saboteur).ToArray();

            var spawned = new List<ITask>();
            Action<ITask> onSpawned = spawned.Add;
            TaskEvents.OnTaskSpawned += onSpawned;
            try
            {
                foreach (AgentController saboteur in squad)
                    saboteur.Scrap();
                yield return new WaitForSeconds(2f);

                Assert.IsTrue(squad.All(s => s.IsDead));
                foreach (SabotageFact fact in board.SabotageFacts)
                {
                    foreach (AgentController saboteur in squad)
                        Assert.AreNotEqual(saboteur.Identity.Id, board.Claims.ClaimedBy(fact.Id), $"{fact.Kind} {fact.Id} is still claimed.");
                }

                Assert.AreEqual(1, spawned.Count, "Saboteur A's keycard, once.");
                Assert.AreEqual(1000, ScoreManager.Current.Rules.PointsFor(ScoreReason.AllSaboteurs), "The all-Saboteurs bonus is awarded once.");

                IEnumerable<AgentController> others = AgentSpawner.Instance.SpawnedAgents.Where(a => a.Type != AgentType.Saboteur);
                Assert.IsTrue(others.All(a => !a.IsDead), "The other agents are untouched.");
                Assert.AreEqual(GameState.Playing, GameClock.Current.State, "The game goes on.");
            }
            finally
            {
                TaskEvents.OnTaskSpawned -= onSpawned;
            }
        }

        [UnityTest]
        public IEnumerator ACutsceneStartingMidAttackFreezesTheSaboteurAndItCarriesOnAfterwards()
        {
            yield return LoadAndStartPlaying();
            AgentController saboteur = Saboteur('A');
            var player = (Component)PlayerState.Current;

            Vector3 near = saboteur.transform.position + new Vector3(4f, 0f, 0f);
            float until = Time.time + 10f;
            while (saboteur.DebugState != "AttackPlayer" && Time.time < until)
            {
                Teleport(player, near);
                yield return null;
            }
            Assert.AreEqual("AttackPlayer", saboteur.DebugState, "A player in sight within 8 m is attacked.");

            GameClock.Current.RequestState(GameState.Cutscene);
            yield return new WaitForSeconds(0.2f);
            Assert.IsTrue(saboteur.IsFrozen, "A cutscene freezes the agents.");
            Vector3 frozenAt = saboteur.transform.position;
            string state = saboteur.DebugState;

            yield return new WaitForSeconds(1f);
            Assert.Less(Vector3.Distance(frozenAt, saboteur.transform.position), 0.01f, "No movement while frozen.");
            Assert.AreEqual(state, saboteur.DebugState, "The brain is not ticked while frozen.");

            GameClock.Current.RequestState(GameState.Playing);
            yield return Until(() => !saboteur.IsFrozen, 5f, "the Saboteur to thaw");
            float resumeUntil = Time.time + 3f;
            while (saboteur.DebugState != "AttackPlayer" && Time.time < resumeUntil)
            {
                Teleport(player, near);
                yield return null;
            }

            Assert.AreEqual("AttackPlayer", saboteur.DebugState, "It carries on attacking once the game resumes.");
            Assert.IsFalse(saboteur.IsDead);
        }
    }
}
