using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.AI.Core;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Tests
{
    /// <summary>
    /// A cutscene or the pause menu must stop the Guard thinking, and play must pick up from
    /// the same game time. The brain itself is never told about it: the controller simply
    /// does not tick it while the game is not Playing.
    /// </summary>
    public sealed class GuardFreezeTests
    {
        /// <summary>A clock that counts game time only while Playing, like the real one.</summary>
        sealed class FakeClock : IGameClock
        {
            readonly List<IGameStateListener> _listeners = new List<IGameStateListener>();

            public GameState State { get; private set; } = GameState.Playing;
            public float GameTime { get; private set; }

            public void AddListener(IGameStateListener listener) => _listeners.Add(listener);
            public void RemoveListener(IGameStateListener listener) => _listeners.Remove(listener);

            public void RequestState(GameState state)
            {
                if (state == State)
                    return;
                GameState previous = State;
                State = state;
                foreach (IGameStateListener listener in _listeners.ToArray())
                    listener.OnGameStateChanged(previous, state);
            }

            public void Advance(float seconds)
            {
                if (State == GameState.Playing)
                    GameTime += seconds;
            }
        }

        /// <summary>A brain that only counts its ticks and remembers the game time of each.</summary>
        sealed class CountingBrain : IAgentBrain
        {
            public int Ticks;
            public readonly List<float> Times = new List<float>();

            public AgentIntent Tick(in AgentContext ctx)
            {
                Ticks++;
                Times.Add(ctx.Time);
                return new AgentIntent { DebugState = "InCover" };
            }

            public void OnGraphChanged(IReadOnlyList<Vector2Int> changedCells) { }
            public void OnStunned(float duration) { }
            public void OnDestroyed() { }
        }

        readonly List<Object> _created = new List<Object>();
        FakeClock _clock;

        [SetUp]
        public void SetUp()
        {
            _clock = new FakeClock();
            GameClock.Publish(_clock);
        }

        [TearDown]
        public void TearDown()
        {
            GameClock.Publish(null);
            foreach (Object created in _created)
                if (created != null)
                    Object.Destroy(created);
            _created.Clear();
        }

        // Runs frames for the given real time, advancing the fake game clock like GameManager does.
        IEnumerator Frames(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
            {
                _clock.Advance(Time.deltaTime);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator Guard_CutsceneFreezesGameTime()
        {
            AgentBodyFixture.Floor(_created);
            var brain = new CountingBrain();
            AgentController guard = AgentBodyFixture.Body("GuardBot", AgentType.Guard, brain, _created,
                new Vector3(0f, 0.1f, 0f));

            yield return Frames(0.4f);
            Assert.Greater(brain.Ticks, 0, "The Guard thinks while the game plays.");

            _clock.RequestState(GameState.Cutscene);
            yield return null;
            Assert.IsTrue(guard.IsFrozen);
            int ticksAtFreeze = brain.Ticks;
            float timeAtFreeze = _clock.GameTime;

            yield return Frames(0.5f);
            Assert.AreEqual(ticksAtFreeze, brain.Ticks, "No brain tick during the cutscene.");
            Assert.AreEqual(timeAtFreeze, _clock.GameTime, "Game time does not move during the cutscene.");

            _clock.RequestState(GameState.Playing);
            yield return Frames(0.4f);
            Assert.IsFalse(guard.IsFrozen);
            Assert.Greater(brain.Ticks, ticksAtFreeze, "It thinks again once play resumes.");
            Assert.LessOrEqual(brain.Times[ticksAtFreeze] - timeAtFreeze, 0.1f,
                "The first tick after the cutscene carries on from the game time it stopped at.");
        }
    }
}
