using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ToyFactory.AI.Core.Grid;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Journey.Cutscenes;
using ToyFactory.Journey.Debugging;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.World;
using Object = UnityEngine.Object;

namespace ToyFactory.Tests
{
    /// <summary>
    /// The debug chapter jump, in the real scenes: jumping to Chapter 3 finishes Chapters 1
    /// and 2 the way the game would, so the Captain is awake and the Control Room is open,
    /// and the player stands at the Storage entrance. Jumps only go forward.
    /// </summary>
    public sealed class ChapterJumpTests
    {
        readonly List<string> _log = new List<string>();
        Action<string> _onSignal;

        [SetUp]
        public void SetUp()
        {
            _log.Clear();
            CutsceneEvents.OnCriticalSignal += _onSignal = id => _log.Add(id);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            CutsceneEvents.OnCriticalSignal -= _onSignal;
            Scene empty = SceneManager.CreateScene("Empty after chapter jump");
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != empty && scene.isLoaded)
                    yield return SceneManager.UnloadSceneAsync(scene);
            }
            // See JourneyFlowTests: GameManager leaves its clock published after it is destroyed.
            if (GameClock.Current is Object clock && clock == null)
                GameClock.Publish(null);
            if (PlayerState.Current is Object player && player == null)
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

        [UnityTest]
        public IEnumerator JumpingToChapterThreeFinishesTheEarlierChaptersAndMovesThePlayer()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return Until(() => ChapterManager.Current != null && ChapterManager.Current.Flow != null && ChapterManager.Current.Flow.HasBegun
                && Object.FindAnyObjectByType<ChapterJump>() != null && AgentSpawner.Instance != null && PlayerState.Current != null,
                30f, "Bootstrap to load every scene and begin the journey");
            ChapterFlow flow = ChapterManager.Current.Flow;
            ChapterJump jump = Object.FindAnyObjectByType<ChapterJump>();

            jump.JumpTo(3);
            Assert.IsTrue(jump.IsJumping);
            yield return Until(() => !jump.IsJumping, 20f, "the jump to finish");

            Assert.AreEqual(3, flow.CurrentChapter);
            Assert.AreEqual(ChapterPhase.Done, flow.GetPhase(1));
            Assert.AreEqual(ChapterPhase.Done, flow.GetPhase(2));
            CollectionAssert.Contains(_log, CutsceneSignals.CaptainWake);
            CollectionAssert.Contains(_log, CutsceneSignals.ControlRoomUnlock);
            Assert.IsTrue(AgentSpawner.Instance.Blackboard.CaptainAwake, "The Captain woke on the way.");
            // The doors slide open over a moment, then the grid lets agents through them.
            yield return Until(() => DoorOpen(new Vector3(20.75f, 0f, 31f)) && DoorOpen(new Vector3(10.5f, 0f, 20.75f)), 5f, "doors 3 and 4 to open");
            Assert.AreEqual(GameState.Playing, GameClock.Current.State, "Back in play, not in a cutscene.");

            yield return null;
            Vector3 player = PlayerState.Current.Position;
            Assert.Less(Vector2.Distance(new Vector2(player.x, player.z), new Vector2(31f, 22.6f)), 1f, "At the Storage entrance.");

            jump.JumpTo(2);
            Assert.IsFalse(jump.IsJumping, "Jumps only go forward.");
            Assert.AreEqual(3, flow.CurrentChapter);
        }
    }
}
