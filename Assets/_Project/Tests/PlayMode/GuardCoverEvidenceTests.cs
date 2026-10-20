using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ToyFactory.AI.Agents.Guard;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Journey.Cutscenes;
using ToyFactory.Runtime.Agents;
using Object = UnityEngine.Object;

namespace ToyFactory.Tests
{
    /// <summary>
    /// IS evidence for Docs/AI/GuardBot.md: is the Guard really hidden while it waits in cover? In the real Painting
    /// Room, with only the Guard awake and the player standing at four spots in turn, every frame the Guard is settled
    /// in cover (InCover, not moving) a line is cast from the player's eye to the Guard's knee, chest and head height.
    /// The share of those frames in which each is in plain view is the measure: a well hidden Guard shows nothing.
    /// </summary>
    [Category("Evidence"), Explicit("About a minute of game time in the real level. Run it on its own.")]
    public sealed class GuardCoverEvidenceTests
    {
        static readonly Vector3[] Spots =
        {
            new Vector3(25f, 0.05f, 4f),
            new Vector3(37f, 0.05f, 15f),
            new Vector3(27f, 0.05f, 17f),
            new Vector3(38f, 0.05f, 6f)
        };

        static readonly float[] Heights = { 0.5f, 1.2f, 2.0f };
        static readonly string[] HeightNames = { "knee (0.5 m)", "chest (1.2 m)", "head (2.0 m)" };

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
        public IEnumerator Evidence_HowHiddenTheGuardIsInCover()
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

            AgentController guard = null;
            foreach (AgentController agent in Object.FindObjectsByType<AgentController>(FindObjectsSortMode.None))
            {
                if (agent.Identity.Type == AgentType.Guard)
                    guard = agent;
                else
                    agent.gameObject.SetActive(false);
            }
            Assert.IsNotNull(guard, "the Guard");
            var brain = (GuardBrain)guard.Brain;

            var player = (Component)PlayerState.Current;
            Component health = player.GetComponent(Type.GetType("ToyFactory.Player.PlayerHealth, Assembly-CSharp"));
            MethodInfo setHealth = health.GetType().GetProperty("Current").GetSetMethod(true);
            var controller = player.GetComponent<CharacterController>();
            int mask = PhysicsCoverVisibility.SightBlockingMask();

            int inCoverFrames = 0;
            var seen = new int[Heights.Length];
            Vector3 lastGuard = guard.transform.position;
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
                        if ((float)health.GetType().GetProperty("Current").GetValue(health) < 40f)
                            setHealth.Invoke(health, new object[] { 100f });

                        Vector3 at = guard.transform.position;
                        bool still = (at - lastGuard).sqrMagnitude < 0.0004f;
                        lastGuard = at;
                        if (brain.StateName == "InCover" && still)
                        {
                            inCoverFrames++;
                            Vector3 eye = player.transform.position + Vector3.up * PhysicsCoverVisibility.PlayerEyeHeight;
                            for (int h = 0; h < Heights.Length; h++)
                            {
                                Vector3 point = at + Vector3.up * Heights[h];
                                if (!Physics.Linecast(eye, point, mask, QueryTriggerInteraction.Ignore))
                                    seen[h]++;
                            }
                        }
                        yield return null;
                    }
                }
            }

            string report = $"Guard settled in cover for {inCoverFrames} frames over 48 s in the Painting Room. In plain view from the player's eye:";
            for (int h = 0; h < Heights.Length; h++)
                report += $"\n  {HeightNames[h]}: {(inCoverFrames > 0 ? 100f * seen[h] / inCoverFrames : 0f):0}% of those frames";
            Debug.Log(report);
            TestContext.Out.WriteLine(report);
            Directory.CreateDirectory("Docs/Evidence/Agents");
            File.AppendAllText("Docs/Evidence/Agents/guard_cover.txt", DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "  " + report + "\n");
            Assert.Greater(inCoverFrames, 0, "the Guard took cover");
        }
    }
}
