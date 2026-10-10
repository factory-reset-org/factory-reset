using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Journey.Cutscenes;
using ToyFactory.UI;

namespace ToyFactory.Tests
{
    /// <summary>
    /// <see cref="ScoreManager"/> turns events into points: with a fake game clock and fake agents for
    /// the rules of the run, and in the real scenes for the wiring (UI.unity holds the manager).
    /// </summary>
    public sealed class ScoreManagerTests
    {
        sealed class FakeClock : IGameClock
        {
            readonly List<IGameStateListener> _listeners = new List<IGameStateListener>();
            public GameState State { get; private set; } = GameState.Playing;
            public float GameTime { get; set; }

            public void AddListener(IGameStateListener listener) => _listeners.Add(listener);
            public void RemoveListener(IGameStateListener listener) => _listeners.Remove(listener);
            public void RequestState(GameState state) => Set(state);

            public void Set(GameState state)
            {
                GameState previous = State;
                State = state;
                foreach (IGameStateListener listener in _listeners.ToArray())
                    listener.OnGameStateChanged(previous, state);
            }
        }

        sealed class FakeAgent : IAgentState
        {
            public FakeAgent(AgentType type, int id) { Type = type; Identity = new AgentIdentity(type, id); }
            public AgentType Type { get; }
            public AgentIdentity Identity { get; }
            public Vector3 Position => Vector3.zero;
            public float Speed => 0f;
            public float TurnRate => 0f;
            public bool IsAttacking => false;
            public bool IsDead => false;
        }

        sealed class FakePlayer : IPlayerState
        {
            public float Health = 0.8f;
            public Vector3 Position => Vector3.zero;
            public Vector3 Velocity => Vector3.zero;
            public Vector3 Forward => Vector3.forward;
            public float SprintSpeed => 7f;
            public bool IsAlive => Health > 0f;
            public float HealthFraction => Health;
            public float AmmoFraction => 1f;
            public bool IsReloading => false;
            public float OverchargeTimeLeft => 0f;
            public float LastShotTime => -1f;
            public void TakeDamage(float amount, int sourceAgentId) { }
        }

        FakeClock _clock;
        GameObject _host;
        ScoreManager _score;

        [SetUp]
        public void SetUp()
        {
            _clock = new FakeClock();
            GameClock.Publish(_clock);
            PlayerState.Publish(new FakePlayer());
            _host = new GameObject("Score");
            _score = _host.AddComponent<ScoreManager>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
                UnityEngine.Object.Destroy(_host);
            GameClock.Publish(null);
            PlayerState.Publish(null);
        }

        void StartRun(float at = 0f)
        {
            _clock.GameTime = at;
            ChapterEvents.RaiseChapterStarted(1);
        }

        // ---- Starting and the simple events

        [Test]
        public void NothingScoresBeforeTheRunStarts()
        {
            ChapterEvents.RaiseTaskCompleted("ch1.lever");
            AgentEvents.RaiseDisabled(new FakeAgent(AgentType.Tracker, 1));

            Assert.AreEqual(0, _score.Score);
            Assert.IsFalse(_score.IsRunning);
        }

        [Test]
        public void ChapterOneStartsTheRun()
        {
            StartRun();

            Assert.IsTrue(_score.IsRunning);
            Assert.AreEqual(RunOutcome.None, _score.Outcome);
            Assert.AreEqual(1, _score.ChaptersReached);
        }

        [Test]
        public void ATaskIsWorthThreeHundredAndASwitchAThousand()
        {
            StartRun();

            ChapterEvents.RaiseTaskCompleted("ch1.lever");
            Assert.AreEqual(300, _score.Score);

            ChapterEvents.RaiseSwitchRestored(1);
            Assert.AreEqual(1300, _score.Score);
        }

        [Test]
        public void BatteryPickupAddsFiftyWhileRunning()
        {
            _score.BatteryPickedUp();
            Assert.AreEqual(0, _score.Score, "No run yet.");

            StartRun();
            _score.BatteryPickedUp();

            Assert.AreEqual(50, _score.Score);
        }

        [Test]
        public void ChaptersReachedFollowsTheChapterEvents()
        {
            StartRun();
            ChapterEvents.RaiseChapterStarted(2);
            ChapterEvents.RaiseChapterStarted(3);

            Assert.AreEqual(3, _score.ChaptersReached);
            Assert.IsTrue(_score.IsRunning, "Later chapters do not restart the run.");
        }

        [Test]
        public void EveryAwardIsAnnouncedForThePointsFeed()
        {
            var seen = new List<ScoreAward>();
            _score.Awarded += seen.Add;
            StartRun();

            ChapterEvents.RaiseTaskCompleted("ch1.lever");
            AgentEvents.RaiseDisabled(new FakeAgent(AgentType.Guard, 2));

            Assert.AreEqual(2, seen.Count);
            Assert.AreEqual(ScoreReason.Task, seen[0].Reason);
            Assert.AreEqual(ScoreReason.Takedown, seen[1].Reason);
        }

        // ---- Takedowns, on game time

        [Test]
        public void KnockedOutAgentsAndScrappedSaboteursScoreTheirTakedowns()
        {
            StartRun();

            AgentEvents.RaiseDisabled(new FakeAgent(AgentType.Tracker, 1));
            _clock.GameTime = 100f;
            AgentEvents.RaiseDestroyed(new FakeAgent(AgentType.Saboteur, 2));

            Assert.AreEqual(150 + 400, _score.Score);
        }

        [Test]
        public void TheComboRunsOnGameTime()
        {
            StartRun();
            AgentEvents.RaiseDisabled(new FakeAgent(AgentType.Tracker, 1));

            _clock.GameTime = 5f;
            AgentEvents.RaiseDisabled(new FakeAgent(AgentType.Tracker, 2));

            Assert.AreEqual(150 + 150 * 2, _score.Score, "5 s of game time apart: x2.");
        }

        [Test]
        public void RepeatTakedownOfARebootedAgentIsWorthLess()
        {
            StartRun();
            var guard = new FakeAgent(AgentType.Guard, 3);
            AgentEvents.RaiseDisabled(guard);
            _clock.GameTime = 100f;

            AgentEvents.RaiseDisabled(guard);

            Assert.AreEqual(250 + 188, _score.Score);
        }

        [Test]
        public void DestroyingAllFourSaboteursPaysTheBonus()
        {
            StartRun();
            for (int i = 1; i <= 4; i++)
            {
                _clock.GameTime = i * 100f;
                AgentEvents.RaiseDestroyed(new FakeAgent(AgentType.Saboteur, i));
            }

            Assert.AreEqual(4 * 400 + 1000, _score.Score);
        }

        // ---- The end of the run

        [Test]
        public void ShuttingDownTheFactoryAndReachingResultsIsAWinWithTheBonus()
        {
            StartRun(10f);
            _clock.GameTime = 310f;
            CutsceneEvents.RaiseCriticalSignal(CutsceneSignals.FactoryShutdown);

            RunOutcome finished = RunOutcome.None;
            _score.RunFinished += o => finished = o;
            _clock.Set(GameState.Results);

            Assert.AreEqual(RunOutcome.Won, _score.Outcome);
            Assert.AreEqual(RunOutcome.Won, finished);
            Assert.IsFalse(_score.IsRunning);
            Assert.AreEqual(300f, _score.RunSeconds, 1e-3f);

            // 2000 + (3000 - 3 x 300) + 10 x 80 HP + no accuracy (no shots reported).
            Assert.AreEqual(2000 + 2100 + 800, _score.Score);
            Assert.AreEqual(ScoreGrade.D, _score.Grade);
        }

        [Test]
        public void ReportedShotsGiveTheAccuracyBonus()
        {
            StartRun(0f);
            _score.ReportShots(20, 15);
            _clock.GameTime = 100f;
            CutsceneEvents.RaiseCriticalSignal(CutsceneSignals.FactoryShutdown);
            _clock.Set(GameState.Results);

            Assert.AreEqual(15 * 75, _score.Rules.PointsFor(ScoreReason.WinAccuracy));
        }

        [Test]
        public void ReachingResultsWithoutTheShutdownIsRecalledWithNoBonus()
        {
            StartRun();
            ChapterEvents.RaiseTaskCompleted("ch1.lever");
            _clock.GameTime = 50f;

            _clock.Set(GameState.Results);

            Assert.AreEqual(RunOutcome.Recalled, _score.Outcome);
            Assert.AreEqual(300, _score.Score, "A recall keeps the points earned and gets no win bonus.");
            Assert.AreEqual(50f, _score.RunSeconds, 1e-3f);
        }

        [Test]
        public void NothingScoresAfterTheRunHasEnded()
        {
            StartRun();
            _clock.Set(GameState.Results);
            int score = _score.Score;

            ChapterEvents.RaiseTaskCompleted("ch1.lever");
            AgentEvents.RaiseDisabled(new FakeAgent(AgentType.Tracker, 1));
            ChapterEvents.RaiseSwitchRestored(1);

            Assert.AreEqual(score, _score.Score);
        }

        [Test]
        public void ResultsIsOnlyCountedOnce()
        {
            StartRun();
            CutsceneEvents.RaiseCriticalSignal(CutsceneSignals.FactoryShutdown);
            int finishes = 0;
            _score.RunFinished += o => finishes++;

            _clock.Set(GameState.Results);
            _clock.Set(GameState.Playing);
            _clock.Set(GameState.Results);

            Assert.AreEqual(1, finishes);
        }

        [Test]
        public void PauseAndCutscenesDoNotCountTowardsTheRunTime()
        {
            StartRun(0f);
            _clock.GameTime = 40f;   // the clock stands still while paused or in a cutscene
            CutsceneEvents.RaiseCriticalSignal(CutsceneSignals.FactoryShutdown);
            _clock.Set(GameState.Results);

            Assert.AreEqual(40f, _score.RunSeconds, 1e-3f);
        }

        [Test]
        public void StartingChapterOneAgainBeginsANewRun()
        {
            StartRun();
            ChapterEvents.RaiseTaskCompleted("ch1.lever");
            ChapterEvents.RaiseChapterStarted(2);
            _clock.Set(GameState.Results);
            Assert.AreEqual(300, _score.Score);

            _clock.Set(GameState.Playing);
            ChapterEvents.RaiseChapterStarted(1);

            Assert.AreEqual(0, _score.Score);
            Assert.IsTrue(_score.IsRunning);
            Assert.AreEqual(RunOutcome.None, _score.Outcome);
            Assert.AreEqual(1, _score.ChaptersReached);
        }

        [Test]
        public void MissingPlayerAtTheEndGivesNoIntegrityBonusAndNoError()
        {
            StartRun();
            PlayerState.Publish(null);
            CutsceneEvents.RaiseCriticalSignal(CutsceneSignals.FactoryShutdown);

            _clock.Set(GameState.Results);

            Assert.AreEqual(RunOutcome.Won, _score.Outcome);
            Assert.AreEqual(0, _score.Rules.PointsFor(ScoreReason.WinIntegrity));
        }

        [Test]
        public void ManagerIgnoresANullAgent()
        {
            StartRun();

            Assert.DoesNotThrow(() => AgentEvents.RaiseDisabled(null));
            Assert.AreEqual(0, _score.Score);
        }

        // ---- The real scenes

        [UnityTest]
        public IEnumerator InTheRealScenesACompletedTaskScoresThreeHundred()
        {
            // Hand the clock back to the real one: the fake was only for the tests above.
            UnityEngine.Object.Destroy(_host);
            _host = null;
            GameClock.Publish(null);
            PlayerState.Publish(null);

            SceneManager.LoadScene("Bootstrap");
            float until = Time.realtimeSinceStartup + 60f;
            while (!(ScoreManager.Current != null && ChapterManager.Current != null && ChapterManager.Current.Flow != null
                && ChapterManager.Current.Flow.HasBegun && CutsceneDirector.Current != null))
            {
                if (Time.realtimeSinceStartup > until)
                    Assert.Fail("Timed out waiting for Bootstrap to load the scenes and the score manager");
                yield return null;
            }

            until = Time.realtimeSinceStartup + 10f;
            while (!CutsceneDirector.Current.IsPlaying)
            {
                if (Time.realtimeSinceStartup > until)
                    Assert.Fail("Timed out waiting for the intro");
                yield return null;
            }

            CutsceneDirector.Current.Skip();
            until = Time.realtimeSinceStartup + 10f;
            while (!(ChapterManager.Current.Flow.CurrentChapter == 1 && ScoreManager.Current.IsRunning))
            {
                if (Time.realtimeSinceStartup > until)
                    Assert.Fail("Timed out waiting for Chapter 1 and the run");
                yield return null;
            }

            ScoreManager score = ScoreManager.Current;
            Assert.AreEqual(0, score.Score);

            ChapterFlow flow = ChapterManager.Current.Flow;
            flow.CompleteTask(flow.CurrentDefinition.Tasks[0].TaskId);
            yield return null;

            Assert.AreEqual(300, score.Score, "A checklist task is worth 300.");
            Assert.AreEqual(1, score.Rules.History.Count);

            // Leave an empty scene behind, so the game does not run under the next test.
            Scene empty = SceneManager.CreateScene("Empty after score " + Guid.NewGuid());
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
    }
}
