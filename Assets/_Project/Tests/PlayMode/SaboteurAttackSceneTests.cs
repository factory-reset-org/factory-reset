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
    /// The Saboteur squad in the real scenes: Bootstrap loads Env, Interactables and Agents, and the
    /// spawner gives each Saboteur the brain with its attack. A player who walks up to a Saboteur
    /// in the open makes it attack, and one who walks away lets it go back to patrolling.
    /// </summary>
    public sealed class SaboteurAttackSceneTests
    {
        const float LoadTimeout = 60f;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;

            // Leave an empty scene behind, so the game does not run under the next test.
            Scene empty = SceneManager.CreateScene("Empty after Saboteur attack " + Guid.NewGuid());
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != empty && scene.isLoaded)
                    yield return SceneManager.UnloadSceneAsync(scene);
            }

            // GameManager publishes itself as the clock and does not clear it when it is destroyed.
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
        public IEnumerator SaboteurAttacksAPlayerWhoWalksUpToItAndPatrolsAgainWhenTheyLeave()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return Until(() => AgentSpawner.Instance != null && AgentSpawner.Instance.SpawnedAgents.Count == 7
                && CutsceneDirector.Current != null && PlayerState.Current != null, LoadTimeout, "Bootstrap to spawn every agent");
            yield return Until(() => CutsceneDirector.Current.IsPlaying, 10f, "the intro");
            CutsceneDirector.Current.Skip();
            yield return Until(() => GameClock.Current != null && GameClock.Current.State == GameState.Playing, 10f, "Playing");

            AgentController saboteur = AgentSpawner.Instance.SpawnedAgents
                .First(a => a.Type == AgentType.Saboteur && a.Identity.SquadLetter == 'A');
            Assert.IsInstanceOf<SaboteurBrain>(saboteur.Brain, "The spawner gives the Saboteur its brain.");
            var player = (Component)PlayerState.Current;

            // 4 m from the Saboteur, held there while it decides.
            Vector3 near = saboteur.transform.position + new Vector3(4f, 0f, 0f);
            float until = Time.time + 10f;
            while (saboteur.DebugState != "AttackPlayer" && Time.time < until)
            {
                Teleport(player, near);
                yield return null;
            }

            Assert.AreEqual("AttackPlayer", saboteur.DebugState, "A player in sight within 8 m is attacked.");
            Assert.AreEqual(AlertLevel.Alert, saboteur.Alert);

            // Gone from sight and far away: it patrols again.
            Teleport(player, saboteur.transform.position + new Vector3(40f, 0f, 0f));
            yield return Until(() => saboteur.DebugState == "Patrol", 5f, "the Saboteur to patrol again");
        }
    }
}
