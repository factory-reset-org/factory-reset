using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ToyFactory.AI.Core.Grid;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Journey.Cutscenes;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.World;

namespace ToyFactory.Tests
{
    /// <summary>
    /// The whole journey in the real scenes: Bootstrap loads Env, Interactables and Agents,
    /// then every task of every chapter is completed through the chapter flow, as the props
    /// would. Checks that each cutscene plays in order, that its Critical signals fire, and
    /// that they change the world: the Captain wakes and the Control Room doors open in ch3,
    /// and the ending hands over to Results.
    /// </summary>
    /// <remarks>
    /// The ch3 cutscene is played through (at 8x speed), so its signals come from the
    /// Timeline's markers; the others are skipped, which fires their signals the way Escape
    /// does. Tasks are completed through <see cref="ChapterFlow.CompleteTask"/>, the call a
    /// prop's completion makes, so the props' own visuals are not part of this test.
    /// </remarks>
    public sealed class JourneyFlowTests
    {
        const float LoadTimeout = 30f;
        static readonly Vector3 Door3 = new Vector3(20.75f, 0f, 31f);
        static readonly Vector3 Door4 = new Vector3(10.5f, 0f, 20.75f);

        readonly List<string> _log = new List<string>();
        Action<string> _onStarted, _onEnded, _onSignal;
        Action<int> _onChapter;

        [SetUp]
        public void SetUp()
        {
            _log.Clear();
            CutsceneEvents.OnCutsceneStarted += _onStarted = id => _log.Add("start:" + id);
            CutsceneEvents.OnCutsceneEnded += _onEnded = id => _log.Add("end:" + id);
            CutsceneEvents.OnCriticalSignal += _onSignal = id => _log.Add("signal:" + id);
            ChapterEvents.OnChapterStarted += _onChapter = n => _log.Add("chapter:" + n);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            CutsceneEvents.OnCutsceneStarted -= _onStarted;
            CutsceneEvents.OnCutsceneEnded -= _onEnded;
            CutsceneEvents.OnCriticalSignal -= _onSignal;
            ChapterEvents.OnChapterStarted -= _onChapter;
            Time.timeScale = 1f;

            // Leave an empty scene behind, so the game does not run under the next test.
            Scene empty = SceneManager.CreateScene("Empty after journey");
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != empty && scene.isLoaded)
                    yield return SceneManager.UnloadSceneAsync(scene);
            }

            // GameManager publishes itself as the clock and does not clear it when it is
            // destroyed, so the next tests would see a dead clock stuck in Results and every
            // agent body would stay frozen.
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

        static bool DoorOpen(Vector3 door)
        {
            GridGraph grid = GridManager.Current;
            return grid != null && grid.IsTraversable(grid.WorldToCell(door));
        }

        static IEnumerator CompleteChapter(ChapterFlow flow)
        {
            ChapterDefinition chapter = flow.CurrentDefinition;
            // The console last: it only counts once the cores are down.
            foreach (TaskDefinition task in chapter.Tasks.OrderBy(t => t.TaskId == ChapterEvents.ConsoleTaskId ? 1 : 0))
            {
                flow.CompleteTask(task.TaskId);
                yield return null;
            }
            if (chapter.SwitchNumber > 0)
            {
                Assert.AreEqual(ChapterPhase.TasksDone, flow.GetPhase(chapter.Index), $"Chapter {chapter.Index}'s switch unseals once its tasks are done.");
                flow.CompleteTask(ChapterFlow.SwitchTaskPrefix + chapter.SwitchNumber);
            }
        }

        IEnumerator SkipWhenItStarts(string id)
        {
            yield return Until(() => CutsceneDirector.Current.IsPlaying && CutsceneDirector.Current.CurrentCutsceneId == id, 10f, "cutscene " + id);
            CutsceneDirector.Current.Skip();
            yield return Until(() => _log.Contains("end:" + id), 5f, "the end of " + id);
        }

        [UnityTest]
        public IEnumerator TheJourneyRunsFromTheIntroToResultsWithEveryCutsceneAndSignal()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return Until(() => ChapterManager.Current != null && ChapterManager.Current.Flow != null && ChapterManager.Current.Flow.HasBegun
                && CutsceneDirector.Current != null && AgentSpawner.Instance != null && AgentSpawner.Instance.SpawnedAgents.Count == 7,
                LoadTimeout, "Bootstrap to load every scene and begin the journey");
            ChapterFlow flow = ChapterManager.Current.Flow;
            AgentSpawner agents = AgentSpawner.Instance;

            // ---- Intro, then Chapter 1. The Control Room is locked and the Captain asleep.
            yield return SkipWhenItStarts("intro");
            Assert.AreEqual(1, flow.CurrentChapter);
            Assert.AreEqual(GameState.Playing, GameClock.Current.State);
            Assert.IsFalse(DoorOpen(Door3), "Door 3 starts closed.");
            Assert.IsFalse(DoorOpen(Door4), "Door 4 starts closed.");
            Assert.IsFalse(agents.Blackboard.CaptainAwake, "The Captain starts dormant.");

            // ---- Chapter 1 -> ch2 -> Chapter 2.
            yield return CompleteChapter(flow);
            yield return SkipWhenItStarts("ch2");
            yield return Until(() => flow.CurrentChapter == 2, 5f, "Chapter 2");

            // ---- Chapter 2 -> ch3, played through: its Timeline fires both signals.
            yield return CompleteChapter(flow);
            yield return Until(() => CutsceneDirector.Current.IsPlaying && CutsceneDirector.Current.CurrentCutsceneId == "ch3", 10f, "cutscene ch3");
            Time.timeScale = 8f;
            yield return Until(() => _log.Contains("end:ch3"), 60f, "ch3 to play to its end");
            Time.timeScale = 1f;
            Assert.Less(_log.IndexOf("signal:" + CutsceneSignals.CaptainWake), _log.IndexOf("end:ch3"), "CaptainWake fired by the Timeline, not on skip.");
            Assert.Less(_log.IndexOf("signal:" + CutsceneSignals.ControlRoomUnlock), _log.IndexOf("end:ch3"));
            Assert.IsTrue(agents.Blackboard.CaptainAwake, "The Captain is awake.");
            yield return Until(() => DoorOpen(Door3) && DoorOpen(Door4), 5f, "doors 3 and 4 to open");
            yield return Until(() => flow.CurrentChapter == 3, 5f, "Chapter 3");

            // ---- Chapter 3 -> ch4 -> Chapter 4.
            yield return CompleteChapter(flow);
            yield return SkipWhenItStarts("ch4");
            CollectionAssert.Contains(_log, "signal:" + CutsceneSignals.CoreShieldsDown);
            yield return Until(() => flow.CurrentChapter == 4, 5f, "Chapter 4");

            // ---- Chapter 4: cores, then the console -> ending -> Results.
            yield return CompleteChapter(flow);
            yield return SkipWhenItStarts("ending");
            CollectionAssert.Contains(_log, "signal:" + CutsceneSignals.FactoryShutdown);
            yield return Until(() => GameClock.Current.State == GameState.Results, 5f, "the Results state");
            Assert.IsTrue(flow.IsFinished, "The journey is finished.");

            // ---- The order, end to end.
            string[] cutscenes = _log.Where(e => e.StartsWith("start:")).ToArray();
            CollectionAssert.AreEqual(new[] { "start:intro", "start:ch2", "start:ch3", "start:ch4", "start:ending" }, cutscenes);
            string[] chapters = _log.Where(e => e.StartsWith("chapter:")).ToArray();
            CollectionAssert.AreEqual(new[] { "chapter:2", "chapter:3", "chapter:4" }, chapters.Where(c => c != "chapter:1"),
                "Each chapter starts once, after the cutscene before it.");
            Assert.AreEqual(1, _log.Count(e => e == "signal:" + CutsceneSignals.CaptainWake), "Each signal fires once.");
        }
    }
}
