using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ToyFactory.AI.Agents.Saboteur;
using ToyFactory.AI.Core;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Cutscenes;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Tests
{
    /// <summary>
    /// Flee in the real scenes: Bootstrap loads Env, Interactables and Agents, and the spawner gives each
    /// Saboteur the brain with its attack and its flight. A hurt Saboteur faced by the player runs; an
    /// unhurt one fights.
    /// </summary>
    public sealed class SaboteurFleeSceneTests
    {
        const float LoadTimeout = 60f;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;

            Scene empty = SceneManager.CreateScene("Empty after Saboteur flee " + Guid.NewGuid());
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

        static void Place(Component body, Vector3 to, Quaternion rotation)
        {
            var controller = body.GetComponent<CharacterController>();
            if (controller != null)
                controller.enabled = false;
            body.transform.SetPositionAndRotation(to, rotation);
            if (controller != null)
                controller.enabled = true;
        }

        // Bootstrap with every agent spawned and the intro skipped.
        static IEnumerator StartPlaying()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return Until(() => AgentSpawner.Instance != null && AgentSpawner.Instance.SpawnedAgents.Count == 7
                && CutsceneDirector.Current != null && PlayerState.Current != null, LoadTimeout, "Bootstrap to spawn every agent");
            // Bootstrap waits on the title screen; this is the player pressing Start.
            if (GameClock.Current != null)
                GameClock.Current.RequestState(GameState.Playing);
            yield return Until(() => CutsceneDirector.Current.IsPlaying, 10f, "the intro");
            CutsceneDirector.Current.Skip();
            yield return Until(() => GameClock.Current != null && GameClock.Current.State == GameState.Playing, 10f, "Playing");
        }

        // The player 4 m east of the Saboteur, looking straight at it, held there while it decides.
        static IEnumerator FaceFromEast(AgentController saboteur, Component player, Func<bool> done, float seconds)
        {
            Vector3 near = saboteur.transform.position + new Vector3(4f, 0f, 0f);
            Quaternion facingWest = Quaternion.LookRotation(Vector3.left, Vector3.up);
            float until = Time.time + seconds;
            while (!done() && Time.time < until)
            {
                Place(player, near, facingWest);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator HurtSaboteurFacedByThePlayerRunsAwayFromThem()
        {
            yield return StartPlaying();
            AgentController saboteur = AgentSpawner.Instance.SpawnedAgents
                .First(a => a.Type == AgentType.Saboteur && a.Identity.SquadLetter == 'A');
            var player = (Component)PlayerState.Current;

            // Hurt to its last hit point.
            while (saboteur.HitPointsLeft > 1)
                saboteur.TakeHit();
            Assert.AreEqual(1, saboteur.HitPointsLeft);

            yield return FaceFromEast(saboteur, player, () => saboteur.DebugState == "Flee", 10f);

            Assert.AreEqual("Flee", saboteur.DebugState, "A hurt Saboteur the player is aiming at runs.");
            Assert.AreEqual(AlertLevel.Alert, saboteur.Alert);

            Vector3 from = saboteur.transform.position;
            Assert.AreNotEqual(0, saboteur.Follower.RemainingWaypointCount, "It has a route to run along.");

            // The player stays 4 m behind it, aiming. It runs until it is out of the player's sight
            // (or out of reach of its refuge) and then goes back to its business.
            float until = Time.time + 3f;
            while (Time.time < until)
            {
                Place(player, saboteur.transform.position + new Vector3(4f, 0f, 0f), Quaternion.LookRotation(Vector3.left, Vector3.up));
                yield return null;
            }

            Assert.Greater(Vector3.Distance(saboteur.transform.position, from), 3f, "It has actually run away.");
        }

        [UnityTest]
        public IEnumerator UnhurtSaboteurFacedByThePlayerFightsInstead()
        {
            yield return StartPlaying();
            AgentController saboteur = AgentSpawner.Instance.SpawnedAgents
                .First(a => a.Type == AgentType.Saboteur && a.Identity.SquadLetter == 'A');
            var player = (Component)PlayerState.Current;

            yield return FaceFromEast(saboteur, player, () => saboteur.DebugState == "AttackPlayer" || saboteur.DebugState == "Flee", 10f);

            Assert.AreEqual("AttackPlayer", saboteur.DebugState);
        }
    }
}
