using System;
using System.Collections;
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

namespace ToyFactory.Tests
{
    /// <summary>
    /// StealBattery in the real level: a Saboteur standing near a battery, with the player short of
    /// ammunition and far away, walks to it and takes it, and the battery leaves the blackboard's facts.
    /// </summary>
    public sealed class SaboteurBatterySceneTests
    {
        const float LoadTimeout = 60f;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;

            // Leave an empty scene behind, so the game does not run under the next test.
            Scene empty = SceneManager.CreateScene("Empty after battery theft " + Guid.NewGuid());
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

        [UnityTest]
        public IEnumerator SaboteurNearABatteryTakesItFromAPlayerShortOfAmmunition()
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

            WorldBlackboard board = AgentSpawner.Instance.Blackboard;
            GridGraph grid = GridManager.Current;
            AgentController saboteur = AgentSpawner.Instance.SpawnedAgents
                .First(a => a.Type == AgentType.Saboteur && a.Identity.SquadLetter == 'A');
            var player = (Component)PlayerState.Current;

            // Spend 14 of the player's 20 shots, so a stolen battery hurts.
            Component battery = player.GetComponent("PlayerBattery");
            Assert.IsNotNull(battery, "The player carries a PlayerBattery.");
            var spend = battery.GetType().GetMethod("TrySpendShot");
            for (int i = 0; i < 14; i++)
                spend.Invoke(battery, null);
            Assert.Less(PlayerState.Current.AmmoFraction, 0.5f);

            // The battery farthest from the player, so the Saboteur wins the race to it.
            Vector3 playerAt = player.transform.position;
            SabotageFact target = board.SabotageFacts
                .Where(f => f.Kind == SabotageFactKind.Battery)
                .OrderByDescending(f => Vector2.Distance(new Vector2(playerAt.x, playerAt.z),
                    new Vector2(grid.CellToWorld(f.Cell).x, grid.CellToWorld(f.Cell).z)))
                .First();
            Assert.IsTrue(SabotageTargetCells.TrySnap(grid, target.Cell, out Vector2Int batteryCell), "The battery sits on or next to a walkable cell.");
            Assert.IsTrue(TryCellNear(grid, batteryCell, out Vector2Int start), "A walkable cell a few metres from the battery.");

            // The Saboteur a few metres from the battery; the player a few metres from the Saboteur, on the far side, in
            // the open and shooting. The Saboteur turns to the noise, sees a player short of ammunition, and must win
            // the race to the battery, which is closer to it than to the player.
            Assert.IsTrue(SaboteurScenePlacement.TryPlayerCell(grid, start, batteryCell, out Vector2Int playerCell), "Open floor for the player near the Saboteur.");
            Vector3 at = grid.CellToWorld(start);
            Teleport(saboteur, new Vector3(at.x, saboteur.transform.position.y, at.z));
            Vector3 stand = grid.CellToWorld(playerCell);
            stand.y = player.transform.position.y;

            bool wasStealing = false;
            float until = Time.time + 25f;
            while (Time.time < until && board.SabotageFacts.Any(f => f.Kind == SabotageFactKind.Battery && f.Id == target.Id))
            {
                Teleport(player, stand);
                PlayerShotNoise.Every(1f);
                wasStealing |= saboteur.DebugState == "StealBattery";
                yield return null;
            }

            Assert.IsFalse(board.SabotageFacts.Any(f => f.Kind == SabotageFactKind.Battery && f.Id == target.Id),
                "The battery was taken.");
            Assert.IsTrue(wasStealing, "Saboteur A went for it (DebugState StealBattery).");
        }
    }
}
