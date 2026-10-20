using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Journey.Cutscenes;
using ToyFactory.Runtime.Agents;
using Object = UnityEngine.Object;

namespace ToyFactory.Tests
{
    /// <summary>
    /// IS evidence for Docs/AI/GuardBot.md: how many of the Guard's shots land, in the real Painting Room. The
    /// player stands still at four spots in turn (6 s each, two rounds) and never fights back; only the Guard is
    /// awake; the player's health is topped up when it falls below 40, so the Guard still sees its hits do harm. Counted from the Guard's weapon: shots fired, and
    /// shots that hit the player rather than something in the way.
    /// </summary>
    [Category("Evidence"), Explicit("About a minute of game time in the real level. Run it on its own.")]
    public sealed class GuardAccuracyEvidenceTests
    {
        static readonly Vector3[] Spots =
        {
            new Vector3(25f, 0.05f, 4f),
            new Vector3(37f, 0.05f, 15f),
            new Vector3(27f, 0.05f, 17f),
            new Vector3(38f, 0.05f, 6f)
        };

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

            if (GameClock.Current is Object clock && clock == null)
                GameClock.Publish(null);
            if (PlayerState.Current is Object player && player == null)
                PlayerState.Publish(null);
        }

        static IEnumerator UntilReal(Func<bool> condition, float seconds, string what)
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
        public IEnumerator Evidence_GuardShotsThatLand()
        {
            AgentSpawner previous = AgentSpawner.Instance;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return UntilReal(() => AgentSpawner.Instance != null && AgentSpawner.Instance != previous
                && ChapterManager.Current != null && ChapterManager.Current.Flow != null && ChapterManager.Current.Flow.HasBegun
                && PlayerState.Current != null, 60f, "the level");
            GameClock.Current.RequestState(GameState.Playing);
            yield return UntilReal(() => CutsceneDirector.Current != null && CutsceneDirector.Current.IsPlaying, 10f, "the intro");
            CutsceneDirector.Current.Skip();
            yield return UntilReal(() => GameClock.Current.State == GameState.Playing, 10f, "Playing");

            AgentWeapon weapon = null;
            foreach (AgentController agent in Object.FindObjectsByType<AgentController>(FindObjectsSortMode.None))
            {
                if (agent.Identity.Type == AgentType.Guard)
                    weapon = agent.GetComponent<AgentWeapon>();
                else
                    agent.gameObject.SetActive(false);
            }
            Assert.IsNotNull(weapon, "the Guard and its weapon");

            var player = (Component)PlayerState.Current;
            Component health = player.GetComponent(Type.GetType("ToyFactory.Player.PlayerHealth, Assembly-CSharp"));
            MethodInfo setHealth = health.GetType().GetProperty("Current").GetSetMethod(true);
            var controller = player.GetComponent<CharacterController>();

            int firedAtStart = weapon.ShotsFired, hitsAtStart = weapon.Hits;
            int seen = weapon.ShotsFired;
            var missedOn = new System.Collections.Generic.Dictionary<string, int>();
            float realCap = Time.realtimeSinceStartup + 300f;
            for (int round = 0; round < 2; round++)
            {
                foreach (Vector3 spot in Spots)
                {
                    controller.enabled = false;
                    controller.transform.position = spot;
                    controller.enabled = true;
                    Physics.SyncTransforms();
                    float until = Time.time + 6f;
                    while (Time.time < until && Time.realtimeSinceStartup < realCap)
                    {
                        // Topped up only when low, so the Guard still sees its hits hurt (it moves on after shots that do not).
                        if ((float)health.GetType().GetProperty("Current").GetValue(health) < 40f)
                            setHealth.Invoke(health, new object[] { 100f });
                        if (weapon.ShotsFired != seen)
                        {
                            seen = weapon.ShotsFired;
                            if (!weapon.LastShotHit)
                            {
                                Vector3 from = weapon.LastShotFrom, to = weapon.LastShotTo;
                                Vector3 dir = (to - from).normalized;
                                string what = Physics.Raycast(from - dir * 0.05f, dir, out RaycastHit hit, Vector3.Distance(from, to) + 0.2f, ~0, QueryTriggerInteraction.Ignore)
                                    ? hit.collider.name + " (" + LayerMask.LayerToName(hit.collider.gameObject.layer) + ", " + Vector3.Distance(from, hit.point).ToString("0.0") + " m)"
                                    : "nothing (short of the player)";
                                missedOn.TryGetValue(what, out int n);
                                missedOn[what] = n + 1;
                            }
                        }
                        yield return null;
                    }
                }
            }

            int fired = weapon.ShotsFired - firedAtStart;
            int hits = weapon.Hits - hitsAtStart;
            string line = $"Guard shots over 48 s in the Painting Room: {fired} fired, {hits} hit the player, {(fired > 0 ? 100f * hits / fired : 0f):0}% landed";
            foreach (var entry in missedOn)
                line += "\n  missed on " + entry.Key + ": " + entry.Value;
            Debug.Log(line);
            TestContext.Out.WriteLine(line);
            Directory.CreateDirectory("Docs/Evidence/Agents");
            File.AppendAllText("Docs/Evidence/Agents/guard_accuracy.txt", DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "  " + line + "\n");
            Assert.Greater(fired, 0, "the Guard engaged");
        }
    }
}
