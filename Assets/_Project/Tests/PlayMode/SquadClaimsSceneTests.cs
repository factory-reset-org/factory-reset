#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ToyFactory.AI.Core.Grid;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.World;

namespace ToyFactory.Tests
{
    /// <summary>
    /// <c>Scenes/Test/Test_SquadClaims.unity</c> in play: four Saboteurs (A to D), two doors on the player's
    /// route and a way round each. The squad splits across the doors (A and B each claim a different one),
    /// walks to them and closes them, and the others close none. This is the live counterpart of the EditMode
    /// <c>SaboteurSquadScenarioTests</c>, which uses the same layout but has no AttackPlayer: here Saboteur A
    /// stands within 8 m of the player in plain sight and attacks instead of taking a door, so B and C share
    /// the doors. Which letters take which job is not asserted, only what must always hold: a door is never
    /// worked on by two Saboteurs, no more than two close doors at once, and both doors end up closed.
    /// </summary>
    /// <remarks>
    /// The scene is not in Build Settings, so it is opened through the editor, which is why this file only
    /// compiles in the editor. Doors: door 1 at cells x 20-21, door 2 at x 40-41, both y 18-21.
    /// </remarks>
    public sealed class SquadClaimsSceneTests
    {
        const string ScenePath = "Assets/_Project/Scenes/Test/Test_SquadClaims.unity";
        const float Timeout = 40f;

        static readonly Vector2Int DoorOneCell = new Vector2Int(20, 19);
        static readonly Vector2Int DoorTwoCell = new Vector2Int(40, 19);

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            Scene empty = SceneManager.CreateScene("Empty after squad " + Guid.NewGuid());
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

        static IEnumerator Open()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return Until(() => AgentSpawner.Instance != null && AgentSpawner.Instance.SpawnedAgents.Count == 4
                && GridManager.Current != null && PlayerState.Current != null, 30f, "the squad scene to spawn four Saboteurs");
        }

        [UnityTest]
        public IEnumerator TheSceneSpawnsTheFourSaboteursAndBothDoorsAreOpenOnTheGrid()
        {
            yield return Open();

            AgentController[] saboteurs = AgentSpawner.Instance.SpawnedAgents.ToArray();
            CollectionAssert.AreEquivalent(new[] { 'A', 'B', 'C', 'D' }, saboteurs.Select(a => a.Identity.SquadLetter).ToArray());
            Assert.IsTrue(saboteurs.All(a => a.Type == AgentType.Saboteur), "Only Saboteurs in this scene.");

            GridGraph grid = GridManager.Current;
            Assert.AreEqual(1, grid.GetNode(DoorOneCell).DoorId);
            Assert.AreEqual(2, grid.GetNode(DoorTwoCell).DoorId);
            Assert.IsFalse(grid.GetNode(DoorOneCell).IsDoorClosed);
            Assert.IsFalse(grid.GetNode(DoorTwoCell).IsDoorClosed);
        }

        [UnityTest]
        public IEnumerator TheSquadSharesTheTwoDoorsBetweenDifferentSaboteursAndClosesBoth()
        {
            yield return Open();
            GridGraph grid = GridManager.Current;
            AgentController[] saboteurs = AgentSpawner.Instance.SpawnedAgents.ToArray();
            var claims = AgentSpawner.Instance.Blackboard.Claims;

            bool splitSeen = false;
            int mostClosingAtOnce = 0;
            float until = Time.realtimeSinceStartup + Timeout;
            while (Time.realtimeSinceStartup < until &&
                   !(grid.GetNode(DoorOneCell).IsDoorClosed && grid.GetNode(DoorTwoCell).IsDoorClosed))
            {
                int? one = claims.ClaimedBy(1);
                int? two = claims.ClaimedBy(2);
                splitSeen |= one.HasValue && two.HasValue && one.Value != two.Value;
                mostClosingAtOnce = Math.Max(mostClosingAtOnce, saboteurs.Count(a => a.DebugState == "CloseDoor"));

                yield return null;
            }

            Assert.IsTrue(grid.GetNode(DoorOneCell).IsDoorClosed, "Door 1 was closed.");
            Assert.IsTrue(grid.GetNode(DoorTwoCell).IsDoorClosed, "Door 2 was closed.");
            Assert.IsTrue(splitSeen, "At one moment the two doors were claimed by two different Saboteurs.");
            Assert.LessOrEqual(mostClosingAtOnce, 2, "Two doors, so never more than two Saboteurs closing doors at once.");

            // Both closed: the claims are let go.
            yield return new WaitForSeconds(1f);
            Assert.IsFalse(claims.ClaimedBy(1).HasValue);
            Assert.IsFalse(claims.ClaimedBy(2).HasValue);
        }
    }
}
#endif
