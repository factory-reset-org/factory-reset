using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Cutscenes;

namespace ToyFactory.Tests.EditMode
{
    public sealed class CutsceneRunnerTests
    {
        sealed class FakeClock : IGameClock
        {
            public readonly List<GameState> Requested = new List<GameState>();
            public GameState State { get; set; } = GameState.Playing;
            public float GameTime { get; set; }
            public void AddListener(IGameStateListener listener) { }
            public void RemoveListener(IGameStateListener listener) { }
            public void RequestState(GameState state)
            {
                Requested.Add(state);
                State = state;
            }
        }

        sealed class FakePlayback : ICutscenePlayback
        {
            public bool HasTimeline = true;
            public readonly List<string> Played = new List<string>();
            public readonly List<bool> Held = new List<bool>();
            public int Stops;
            public bool IsFinished { get; set; }
            public bool Play(CutsceneDefinition cutscene)
            {
                if (!HasTimeline)
                    return false;
                Played.Add(cutscene.Id);
                IsFinished = false;
                return true;
            }
            public void SetHeld(bool held) => Held.Add(held);
            public void Stop() => Stops++;
        }

        const float Delay = CutsceneRunner.DefaultStartDelay;

        // Every event in the order it was raised, e.g. "started:ch3", "signal:CaptainWake", "ended:ch3",
        // with the clock's state at that moment appended by the state listeners below.
        readonly List<string> _log = new List<string>();
        Action<string> _onStarted, _onEnded, _onSignal;
        FakeClock _clock;
        FakePlayback _playback;

        [SetUp]
        public void SetUp()
        {
            _log.Clear();
            _clock = new FakeClock();
            _playback = new FakePlayback();
            CutsceneEvents.OnCutsceneStarted += _onStarted = id => _log.Add("started:" + id);
            CutsceneEvents.OnCutsceneEnded += _onEnded = id => _log.Add("ended:" + id + "@" + _clock.State);
            CutsceneEvents.OnCriticalSignal += _onSignal = id => _log.Add("signal:" + id);
        }

        [TearDown]
        public void TearDown()
        {
            CutsceneEvents.OnCutsceneStarted -= _onStarted;
            CutsceneEvents.OnCutsceneEnded -= _onEnded;
            CutsceneEvents.OnCriticalSignal -= _onSignal;
        }

        CutsceneRunner Create(bool withClock = true) =>
            new CutsceneRunner(CutsceneDefinition.JourneyDefaults(), _playback, () => withClock ? _clock : null);

        [Test]
        public void JourneyDefaultsMatchTheChapterDataAndThePlan()
        {
            CutsceneDefinition[] all = CutsceneDefinition.JourneyDefaults();
            Assert.AreEqual(5, all.Length);
            Assert.AreEqual("ch2", Array.Find(all, c => c.SwitchNumber == 1).Id);
            Assert.AreEqual("ch3", Array.Find(all, c => c.SwitchNumber == 2).Id);
            Assert.AreEqual("ch4", Array.Find(all, c => c.SwitchNumber == 3).Id);
            CollectionAssert.AreEquivalent(new[] { CutsceneSignals.CaptainWake, CutsceneSignals.ControlRoomUnlock },
                Array.Find(all, c => c.Id == "ch3").CriticalSignals);
            CutsceneDefinition ending = Array.Find(all, c => c.Trigger == CutsceneTrigger.ConsoleCompleted);
            Assert.AreEqual(GameState.Results, ending.StateAfter);
            CollectionAssert.AreEqual(new[] { CutsceneSignals.FactoryShutdown }, ending.CriticalSignals);
        }

        [Test]
        public void SwitchCutsceneStartsAfterTheDelayAndFreezesTheGame()
        {
            CutsceneRunner runner = Create();

            Assert.IsTrue(runner.RequestForSwitch(1, Delay));
            runner.Tick(1.2f);
            Assert.IsFalse(runner.IsPlaying, "Still within the 1.3 s delay.");
            Assert.IsEmpty(_log);

            runner.Tick(0.2f);
            Assert.IsTrue(runner.IsPlaying);
            Assert.AreEqual("ch2", runner.CurrentId);
            CollectionAssert.AreEqual(new[] { "started:ch2" }, _log);
            CollectionAssert.AreEqual(new[] { GameState.Cutscene }, _clock.Requested);
            CollectionAssert.AreEqual(new[] { "ch2" }, _playback.Played);
        }

        [Test]
        public void DelayDoesNotCountWhilePaused()
        {
            CutsceneRunner runner = Create();
            runner.RequestForSwitch(1, Delay);

            _clock.State = GameState.Paused;
            runner.Tick(5f);
            Assert.IsFalse(runner.IsPlaying);

            _clock.State = GameState.Playing;
            runner.Tick(1.2f);
            Assert.IsFalse(runner.IsPlaying, "The paused 5 s did not count.");
            runner.Tick(0.2f);
            Assert.IsTrue(runner.IsPlaying);
        }

        [Test]
        public void TimelineEndRaisesEndedThenHandsTheGameBack()
        {
            CutsceneRunner runner = Create();
            runner.RequestForSwitch(1, 0f);
            runner.Tick(0f);

            runner.Tick(10f);
            Assert.IsTrue(runner.IsPlaying, "A Timeline ends when it finishes, not on a timer.");

            _playback.IsFinished = true;
            runner.Tick(0.016f);

            Assert.IsFalse(runner.IsPlaying);
            CollectionAssert.AreEqual(new[] { "started:ch2", "ended:ch2@Cutscene" }, _log,
                "Ended is raised while still in Cutscene, so the next chapter starts before the agents unfreeze.");
            CollectionAssert.AreEqual(new[] { GameState.Cutscene, GameState.Playing }, _clock.Requested);
        }

        [Test]
        public void CutsceneWithoutATimelineHoldsForItsPlaceholderTime()
        {
            _playback.HasTimeline = false;
            CutsceneRunner runner = Create();
            runner.RequestForSwitch(1, 0f);
            runner.Tick(0f);

            runner.Tick(1.9f);
            Assert.IsTrue(runner.IsPlaying);
            runner.Tick(0.2f);
            Assert.IsFalse(runner.IsPlaying);
            Assert.AreEqual("ended:ch2@Cutscene", _log[_log.Count - 1]);
        }

        [Test]
        public void SignalReachedOnTheTimelineIsRaisedOnce()
        {
            CutsceneRunner runner = Create();
            runner.RequestForSwitch(2, 0f);
            runner.Tick(0f);

            runner.SignalReached(CutsceneSignals.CaptainWake);
            runner.SignalReached(CutsceneSignals.CaptainWake);

            CollectionAssert.AreEqual(new[] { "started:ch3", "signal:CaptainWake" }, _log);
        }

        [Test]
        public void SkipFiresOnlyTheCriticalSignalsNotYetReached()
        {
            CutsceneRunner runner = Create();
            runner.RequestForSwitch(2, 0f);
            runner.Tick(0f);
            runner.SignalReached(CutsceneSignals.CaptainWake);

            runner.Skip();

            Assert.IsFalse(runner.IsPlaying);
            Assert.AreEqual(1, _playback.Stops);
            CollectionAssert.AreEqual(new[]
            {
                "started:ch3",
                "signal:CaptainWake",
                "signal:ControlRoomUnlock",
                "ended:ch3@Cutscene",
            }, _log);
            Assert.AreEqual(GameState.Playing, _clock.State);
        }

        [Test]
        public void TimelineThatMissesASignalStillFiresItAtTheEnd()
        {
            CutsceneRunner runner = Create();
            runner.RequestForSwitch(3, 0f);
            runner.Tick(0f);

            _playback.IsFinished = true;
            runner.Tick(0.016f);

            CollectionAssert.AreEqual(new[] { "started:ch4", "signal:CoreShieldsDown", "ended:ch4@Cutscene" }, _log);
        }

        [Test]
        public void PauseHoldsTheCutsceneAndIgnoresSkip()
        {
            _playback.HasTimeline = false;
            CutsceneRunner runner = Create();
            runner.RequestForSwitch(1, 0f);
            runner.Tick(0f);

            _clock.State = GameState.Paused;
            runner.Tick(10f);
            runner.Skip();
            Assert.IsTrue(runner.IsPlaying, "Paused: neither the hold time nor a skip ends it.");

            _clock.State = GameState.Cutscene;
            runner.Tick(2.1f);
            Assert.IsFalse(runner.IsPlaying);
        }

        [Test]
        public void PauseHoldsAndResumesTheTimeline()
        {
            CutsceneRunner runner = Create();
            runner.RequestForSwitch(1, 0f);
            runner.Tick(0f);

            _clock.State = GameState.Paused;
            runner.Tick(0.1f);
            runner.Tick(0.1f);
            _clock.State = GameState.Cutscene;
            runner.Tick(0.1f);

            CollectionAssert.AreEqual(new[] { true, false }, _playback.Held, "Told once on each change.");
        }

        [Test]
        public void EndingLeavesTheGameOnTheResultsScreen()
        {
            CutsceneRunner runner = Create();
            Assert.IsTrue(runner.RequestForTrigger(CutsceneTrigger.ConsoleCompleted, 0f));
            runner.Tick(0f);
            runner.Skip();

            Assert.AreEqual("ended:ending@Cutscene", _log[_log.Count - 1]);
            Assert.AreEqual(GameState.Results, _clock.State);
        }

        [Test]
        public void SkippedEndingStillShutsTheFactoryDown()
        {
            CutsceneRunner runner = Create();
            runner.RequestForTrigger(CutsceneTrigger.ConsoleCompleted, 0f);
            runner.Tick(0f);

            runner.Skip();

            CollectionAssert.AreEqual(new[]
            {
                "started:ending",
                "signal:" + CutsceneSignals.FactoryShutdown,
                "ended:ending@Cutscene",
            }, _log, "The lights go down before the results screen, even when skipped.");
        }

        [Test]
        public void IntroCanStartFromTheTitleScreen()
        {
            _clock.State = GameState.Title;
            CutsceneRunner runner = Create();

            Assert.IsTrue(runner.Request("intro", 0f));
            runner.Tick(0f);

            Assert.IsTrue(runner.IsPlaying);
        }

        [Test]
        public void ASecondCutsceneWaitsForTheFirstAndDuplicatesAreIgnored()
        {
            CutsceneRunner runner = Create();
            runner.RequestForSwitch(1, 0f);
            runner.Tick(0f);

            Assert.IsTrue(runner.RequestForSwitch(2, 0f));
            Assert.IsFalse(runner.RequestForSwitch(1, 0f), "Already playing.");
            Assert.IsFalse(runner.RequestForSwitch(2, 0f), "Already queued.");

            runner.Skip();
            runner.Tick(0f);
            Assert.AreEqual("ch3", runner.CurrentId);
        }

        [Test]
        public void UnknownCutsceneStillStartsAndEndsSoTheJourneyCarriesOn()
        {
            _playback.HasTimeline = false;
            CutsceneRunner runner = Create();
            LogAssert.Expect(LogType.Warning, new Regex("No cutscene \"mystery\""));

            Assert.IsFalse(runner.Request("mystery", 0f));
            runner.Tick(0f);
            runner.Tick(0f);

            CollectionAssert.AreEqual(new[] { "started:mystery", "ended:mystery@Cutscene" }, _log);
        }

        [Test]
        public void SwitchWithoutACutsceneIsReported()
        {
            CutsceneRunner runner = Create();
            LogAssert.Expect(LogType.Error, new Regex("switch 9"));

            Assert.IsFalse(runner.RequestForSwitch(9, 0f));
        }

        [Test]
        public void WithoutAGameClockCutscenesStillPlay()
        {
            CutsceneRunner runner = Create(withClock: false);
            runner.RequestForSwitch(1, 0f);
            runner.Tick(0f);
            Assert.IsTrue(runner.IsPlaying);

            runner.Skip();
            CollectionAssert.AreEqual(new[] { "started:ch2", "ended:ch2@Playing" }, _log);
            Assert.IsEmpty(_clock.Requested);
        }
    }
}
