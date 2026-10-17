using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Journey.Cutscenes;
using ToyFactory.Runtime.Agents;
using ToyFactory.UI;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace ToyFactory.Tests
{
    /// <summary>
    /// A scripted playthrough for Docs/PlaythroughLog.md: the whole game from the title screen to the Results
    /// screen in the real scenes, with the real agents awake. Every task is done through the prop itself (the
    /// lever is used, the crate rides the belt to the plate, fuses and the keycard are walked into, targets and
    /// cores are shot, the terminal and console are held, relays are pressed in the board's order, Saboteur A is
    /// shot until it drops the keycard), the player is moved to each prop, and every cutscene is skipped as soon
    /// as it starts. It is not a hand-played run: no aiming, no route choice. The player's health is topped up
    /// after every hit so the run reaches the end, and every hit is counted by who dealt it. The report goes to
    /// the test output and to Docs/Evidence/Playthrough/, with one screenshot per chapter.
    /// </summary>
    [Category("Evidence"), Explicit("A full scripted run of the game: about three minutes. Run it on its own.")]
    public sealed class PlaythroughEvidenceTests
    {
        const string EvidenceFolder = "Docs/Evidence/Playthrough";

        readonly StringBuilder _log = new StringBuilder();
        readonly Dictionary<string, float> _damageBySource = new Dictionary<string, float>();
        readonly List<float> _frameTimes = new List<float>();
        float _runStart;
        Component _health;
        PropertyInfo _healthCurrent;

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

        static Type PropType(string name) => Type.GetType("ToyFactory.Interaction." + name + ",Assembly-CSharp");

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

        // Waits in game time, skipping any cutscene that starts, and records frame times while playing.
        IEnumerator Until(Func<bool> condition, float gameSeconds, string what, Func<string> detail = null)
        {
            float until = Time.time + gameSeconds;
            float realCap = Time.realtimeSinceStartup + 240f;
            while (!condition())
            {
                SkipCutscene();
                if (GameClock.Current != null && GameClock.Current.State == GameState.Playing)
                    _frameTimes.Add(Time.unscaledDeltaTime);
                if (Time.time > until || Time.realtimeSinceStartup > realCap)
                    Assert.Fail("Timed out waiting for " + what + (detail != null ? " (" + detail() + ")" : "") + "\n" + _log);
                yield return null;
            }
        }

        static void SkipCutscene()
        {
            CutsceneDirector director = CutsceneDirector.Current;
            if (director != null && director.IsPlaying)
                director.Skip();
        }

        IEnumerator WaitPlaying(float seconds)
        {
            float until = Time.time + seconds;
            yield return Until(() => Time.time >= until && GameClock.Current.State == GameState.Playing, seconds + 30f, "time to pass");
        }

        static void MovePlayer(Vector3 position)
        {
            var controller = ((Component)PlayerState.Current).GetComponent<CharacterController>();
            controller.enabled = false;
            controller.transform.position = position;
            controller.enabled = true;
            Physics.SyncTransforms();
        }

        static Component TaskById(string typeName, string id)
        {
            foreach (Object each in Object.FindObjectsByType(PropType(typeName), FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var prop = (Component)each;
                if ((string)prop.GetType().GetProperty("Id").GetValue(prop) == id)
                    return prop;
            }
            return null;
        }

        static bool Done(Component task) => (bool)task.GetType().GetProperty("IsCompleted").GetValue(task);

        static int Chapter => ChapterManager.Current.Flow.CurrentChapter;

        void Note(string line)
        {
            string stamped = $"{FormatTime(Time.time - _runStart)}  {line}";
            _log.AppendLine(stamped);
            Debug.Log("[Playthrough] " + stamped);
        }

        static string FormatTime(float seconds) => $"{(int)(seconds / 60f):00}:{(int)(seconds % 60f):00}";

        void HandleDamaged(float amount, int sourceId)
        {
            string who = sourceId < 0 ? "trap or unknown" : "agent " + sourceId;
            foreach (AgentController agent in Object.FindObjectsByType<AgentController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (agent.Identity.Id == sourceId)
                    who = agent.Identity.Type.ToString();
            }
            _damageBySource.TryGetValue(who, out float total);
            _damageBySource[who] = total + amount;
            // Topped up so the scripted run reaches the end; the hit is still counted above.
            _healthCurrent.GetSetMethod(true).Invoke(_health, new object[] { 100f });
        }

        void Screenshot(string name)
        {
            Directory.CreateDirectory(EvidenceFolder);
            Camera camera = Camera.main;
            if (camera == null)
                return;
            var target = new RenderTexture(1280, 720, 24);
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = null;
            RenderTexture.active = target;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(EvidenceFolder, name + ".png"), image.EncodeToPNG());
            Object.Destroy(image);
            target.Release();
        }

        string FrameSummary()
        {
            if (_frameTimes.Count == 0)
                return "no frames";
            float total = 0f, worst = 0f;
            foreach (float t in _frameTimes)
            {
                total += t;
                worst = Mathf.Max(worst, t);
            }
            string summary = $"average {(_frameTimes.Count / total):0} fps, lowest {(1f / Mathf.Max(worst, 0.0001f)):0} fps over {_frameTimes.Count} frames";
            _frameTimes.Clear();
            return summary;
        }

        IEnumerator UseSwitch(int number)
        {
            Component sw = GameObject.Find("Switch_" + number).GetComponent(PropType("ControlSwitch"));
            var unsealed = sw.GetType().GetProperty("IsUnsealed");
            yield return Until(() => (bool)unsealed.GetValue(sw), 20f, "switch " + number + " to unseal");
            MovePlayer(sw.transform.position - sw.transform.forward * 1.5f + Vector3.up * 0.05f);
            yield return null;
            sw.GetType().GetMethod("Interact").Invoke(sw, null);
            Assert.IsTrue(Done(sw), "switch " + number + " restored");
            Note($"Switch {number} restored");
        }

        IEnumerator HoldUntilDone(Component task, float seconds)
        {
            MovePlayer(task.transform.position - task.transform.forward * 1.4f + Vector3.up * 0.05f);
            yield return null;
            task.GetType().GetMethod("Interact").Invoke(task, null);
            MethodInfo hold = task.GetType().GetMethod("Hold");
            yield return Until(() =>
            {
                if (GameClock.Current.State == GameState.Playing)
                    hold.Invoke(task, new object[] { Time.deltaTime });
                return Done(task);
            }, seconds + 20f, task.name + " to be held to the end");
        }

        [UnityTest]
        public IEnumerator Evidence_ScriptedPlaythroughFromTitleToResults()
        {
            AgentSpawner previous = AgentSpawner.Instance;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return UntilReal(() => AgentSpawner.Instance != null && AgentSpawner.Instance != previous
                && ChapterManager.Current != null && ChapterManager.Current.Flow != null && ChapterManager.Current.Flow.HasBegun
                && PlayerState.Current != null && ScoreManager.Current != null, 60f, "the level and the title screen");
            Assert.AreEqual(GameState.Title, GameClock.Current.State, "the game waits on the title screen");

            _health = ((Component)PlayerState.Current).GetComponent(Type.GetType("ToyFactory.Player.PlayerHealth, Assembly-CSharp"));
            _healthCurrent = _health.GetType().GetProperty("Current");
            var damaged = _health.GetType().GetEvent("OnDamaged");
            var handler = Delegate.CreateDelegate(damaged.EventHandlerType, this, GetType().GetMethod(nameof(HandleDamaged), BindingFlags.NonPublic | BindingFlags.Instance));
            damaged.AddEventHandler(_health, handler);

            // Start, and skip the intro.
            _runStart = Time.time;
            GameClock.Current.RequestState(GameState.Playing);
            yield return Until(() => GameClock.Current.State == GameState.Playing && Chapter == 1, 60f, "the intro to end");
            Note("Start pressed, intro skipped, Chapter 1 begins");
            yield return WaitPlaying(1f);
            Screenshot("ch1_assembly");

            // ---- Chapter 1: lever, crate on the belt to the plate, three fuses, switch 1.
            Component lever = (Component)Object.FindAnyObjectByType(PropType("ConveyorLever"));
            MovePlayer(lever.transform.position - lever.transform.forward * 1.5f + Vector3.up * 0.05f);
            yield return null;
            lever.GetType().GetMethod("Interact").Invoke(lever, null);
            Assert.IsTrue(Done(lever), "the lever started the belts");
            Note("ch1.lever: conveyor lever pulled, belts running");

            Component plate = (Component)Object.FindAnyObjectByType(PropType("PressurePlate"));
            GameObject crate = GameObject.Find("Crate_1");
            Component box = crate.GetComponent(PropType("PushableBox"));
            MovePlayer(new Vector3(10.5f, 0.05f, 12f));   // out of the crate's way along the belt
            yield return null;
            box.GetType().GetMethod("Teleport").Invoke(box, new object[] { new Vector3(10f, 0.5f, 17.5f), Quaternion.identity });
            yield return Until(() => Done(plate), 40f, "the crate to ride the belt and latch the plate",
                () => "crate at " + crate.transform.position + ", belts running " + Done(lever));
            Note("ch1.plate: crate put on the top belt, rode to the stop and latched the plate");

            for (int n = 1; n <= 3; n++)
            {
                Component fuse = TaskById("Collectible", "ch1.fuse." + n);
                Assert.IsNotNull(fuse, "fuse " + n);
                MovePlayer(fuse.transform.position + Vector3.down * 0.9f);
                Component f = fuse;
                yield return Until(() => Done(f), 10f, "fuse " + n + " to be picked up");
                Note($"ch1.fuse.{n}: fuse {n} picked up");
            }

            yield return UseSwitch(1);
            Note("Chapter 1 frame rate: " + FrameSummary());
            yield return Until(() => Chapter == 2 && GameClock.Current.State == GameState.Playing, 60f, "Chapter 2");
            Note("Cutscene skipped, Chapter 2 begins");
            yield return WaitPlaying(1f);
            Screenshot("ch2_painting");

            // ---- Chapter 2: four spinning targets within the window, the terminal, switch 2.
            Component group = (Component)Object.FindAnyObjectByType(PropType("TargetGroup"));
            foreach (Object each in Object.FindObjectsByType(PropType("SpinningTarget"), FindObjectsSortMode.None))
            {
                var target = (Component)each;
                target.GetType().GetMethod("TakeHit").Invoke(target, null);
                yield return WaitPlaying(0.5f);
            }
            yield return Until(() => Done(group), 10f, "the targets task");
            Note("ch2.targets: four spinning targets hit inside the 12 s window");

            yield return HoldUntilDone(TaskById("HoldTask", "ch2.terminal"), 6f);
            Note("ch2.terminal: terminal held for 6 s, hacked");
            yield return UseSwitch(2);
            Note("Chapter 2 frame rate: " + FrameSummary());
            yield return Until(() => Chapter == 3 && GameClock.Current.State == GameState.Playing, 60f, "Chapter 3");
            Note("Cutscene skipped, Chapter 3 begins");
            yield return WaitPlaying(1f);
            Screenshot("ch3_storage");

            // ---- Chapter 3: relays in the board's order, Saboteur A's keycard, switch 3.
            Component board = (Component)Object.FindAnyObjectByType(PropType("RelayBoard"));
            MovePlayer(board.transform.position - board.transform.forward * 2f + Vector3.up * 0.05f);
            var isRead = board.GetType().GetProperty("IsRead");
            yield return Until(() => (bool)isRead.GetValue(board), 10f, "the board to be read");
            Assert.IsTrue(ChapterManager.Current.Flow.TryGetSequenceOrder("ch3.relays", out IReadOnlyList<int> order), "the relay order");
            var relays = (Array)board.GetType().GetField("relays", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(board);
            foreach (int number in order)
            {
                foreach (Component relay in relays)
                {
                    if ((int)relay.GetType().GetProperty("Number").GetValue(relay) == number)
                        relay.GetType().GetMethod("Interact").Invoke(relay, null);
                }
                yield return WaitPlaying(0.3f);
            }
            yield return Until(() => Done(board), 10f, "the relay task");
            Note($"ch3.relays: relays pressed in the board's order ({string.Join(", ", order)})");

            AgentController carrier = null;
            foreach (AgentController agent in Object.FindObjectsByType<AgentController>(FindObjectsSortMode.None))
            {
                if (agent.Identity.Type == AgentType.Saboteur && agent.Identity.SquadLetter == 'A')
                    carrier = agent;
            }
            Assert.IsNotNull(carrier, "Saboteur A");
            int shots = 0;
            yield return Until(() =>
            {
                if (!carrier.IsDead && GameClock.Current.State == GameState.Playing)
                {
                    carrier.TakeHit();
                    shots++;
                }
                return carrier.IsDead;
            }, 30f, "Saboteur A to be scrapped");
            Note($"Saboteur A scrapped ({shots} hits taken), keycard dropped");
            Component keycard = null;
            yield return Until(() => (keycard = TaskById("Collectible", "ch3.keycard")) != null, 5f, "the keycard to drop");
            MovePlayer(keycard.transform.position + Vector3.down * 0.9f);
            Component k = keycard;
            yield return Until(() => Done(k), 10f, "the keycard to be picked up");
            Note("ch3.keycard: keycard picked up");
            yield return UseSwitch(3);
            Note("Chapter 3 frame rate: " + FrameSummary());
            yield return Until(() => Chapter == 4 && GameClock.Current.State == GameState.Playing, 60f, "Chapter 4");
            Note("Cutscene skipped (the Captain wakes, doors 3 and 4 unlock), Chapter 4 begins");
            yield return WaitPlaying(1f);
            Screenshot("ch4_control");

            // ---- Chapter 4: three power cores (8 hits each once unshielded), then the console.
            foreach (Object each in Object.FindObjectsByType(PropType("PowerCore"), FindObjectsSortMode.None))
            {
                var core = (Component)each;
                var shielded = core.GetType().GetProperty("IsShielded");
                yield return Until(() => !(bool)shielded.GetValue(core), 20f, core.name + " to lose its shield");
                MovePlayer(core.transform.position - core.transform.forward * 3f + new Vector3(0f, -core.transform.position.y + 0.05f, 0f));
                MethodInfo hit = core.GetType().GetMethod("TakeHit");
                yield return Until(() =>
                {
                    if (GameClock.Current.State == GameState.Playing)
                        hit.Invoke(core, null);
                    return Done(core);
                }, 20f, core.name + " to be destroyed");
                string id = (string)core.GetType().GetProperty("Id").GetValue(core);
                Note($"{id}: {core.name} destroyed");
            }

            yield return HoldUntilDone(TaskById("HoldTask", "console"), 3f);
            Note("console: final console held, factory reset");
            Note("Chapter 4 frame rate: " + FrameSummary());
            yield return Until(() => GameClock.Current.State == GameState.Results, 90f, "the Results screen");
            ScoreManager score = ScoreManager.Current;
            Note($"Results: {score.Outcome}, score {score.Score:N0}, grade {score.Grade}, run time {FormatTime(score.RunSeconds)}");
            Screenshot("results");

            var report = new StringBuilder();
            report.AppendLine("Scripted playthrough, title to results");
            report.Append(_log);
            report.AppendLine("Damage the player took, by source (health was topped up after each hit):");
            if (_damageBySource.Count == 0)
                report.AppendLine("  none");
            foreach (KeyValuePair<string, float> entry in _damageBySource)
                report.AppendLine($"  {entry.Key}: {entry.Value:0}");
            Directory.CreateDirectory(EvidenceFolder);
            File.WriteAllText(Path.Combine(EvidenceFolder, "scripted_playthrough.txt"), report.ToString());
            Debug.Log(report.ToString());
            TestContext.Out.WriteLine(report.ToString());

            Assert.AreEqual(RunOutcome.Won, score.Outcome, "the run is won");
            damaged.RemoveEventHandler(_health, handler);
        }
    }
}
