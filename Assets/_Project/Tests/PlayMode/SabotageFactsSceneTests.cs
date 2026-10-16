using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Tests
{
    /// <summary>
    /// The trap and battery facts in the real level: every trap and battery is listed, ids are unique
    /// across doors, traps and batteries (the squad claims a target by id alone), and the facts follow
    /// a trap being armed and a battery being taken.
    /// </summary>
    public sealed class SabotageFactsSceneTests
    {
        // Leaves nothing behind: the tests that run next load Bootstrap themselves and must not
        // find this level's spawner, clock or player still alive.
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

            if (GameClock.Current is UnityEngine.Object clock && clock == null)
                GameClock.Publish(null);
            if (PlayerState.Current is UnityEngine.Object player && player == null)
                PlayerState.Publish(null);
        }

        const float LoadTimeout = 60f;

        static IEnumerator Until(Func<bool> condition, float seconds, string what)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > until)
                    Assert.Fail("Timed out waiting for " + what);
                yield return null;
            }
        }

        static IEnumerator LoadLevel()
        {
            // A previous test's level can still be alive for a frame; wait for this load's own spawner.
            AgentSpawner previous = AgentSpawner.Instance;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return Until(() => AgentSpawner.Instance != null && AgentSpawner.Instance != previous
                && ChapterManager.Current != null && ChapterManager.Current.Flow != null
                && ChapterManager.Current.Flow.HasBegun
                && AgentSpawner.Instance.Blackboard.SabotageFacts.Count > 0, LoadTimeout, "the level and the sabotage facts");
        }

        static bool Has(WorldBlackboard board, SabotageFactKind kind, int id, out SabotageFact found)
        {
            for (int i = 0; i < board.SabotageFacts.Count; i++)
            {
                SabotageFact fact = board.SabotageFacts[i];
                if (fact.Kind == kind && fact.Id == id)
                {
                    found = fact;
                    return true;
                }
            }
            found = default;
            return false;
        }

        static SabotageFact FirstOf(WorldBlackboard board, SabotageFactKind kind)
        {
            for (int i = 0; i < board.SabotageFacts.Count; i++)
            {
                if (board.SabotageFacts[i].Kind == kind)
                    return board.SabotageFacts[i];
            }
            Assert.Fail("no " + kind + " fact");
            return default;
        }

        [UnityTest]
        public IEnumerator EveryTrapAndBatteryIsListed_WithIdsUniqueAcrossAllThreeKinds()
        {
            yield return LoadLevel();
            WorldBlackboard board = AgentSpawner.Instance.Blackboard;

            var targets = new List<SabotageTargetInfo>();
            SabotageTargets.CopyTo(targets);
            var seen = new HashSet<int>();
            int traps = 0, batteries = 0;
            foreach (SabotageTargetInfo t in targets)
            {
                Assert.IsTrue(seen.Add(t.Id), $"{t.Kind} {t.Id} shares its id with another door, trap or battery");
                if (t.Kind == SabotageKind.Trap)
                    traps++;
                if (t.Kind == SabotageKind.Battery)
                    batteries++;
            }

            Assert.AreEqual(4, traps, "four traps in the level");
            Assert.AreEqual(6, batteries, "six batteries in the level");
            Assert.AreEqual(traps + batteries, board.SabotageFacts.Count, "each one is a fact; doors are not");
        }

        [UnityTest]
        public IEnumerator ArmingATrapAndTakingABattery_ShowsInTheFacts()
        {
            yield return LoadLevel();
            WorldBlackboard board = AgentSpawner.Instance.Blackboard;

            SabotageFact trap = FirstOf(board, SabotageFactKind.Trap);
            SabotageFact battery = FirstOf(board, SabotageFactKind.Battery);
            Assert.IsTrue(trap.Available, "traps start disarmed");
            int before = board.SabotageFacts.Count;

            Assert.IsTrue(SabotageTargets.TryGet(SabotageKind.Trap, trap.Id, out ISabotageable trapTarget, out _));
            trapTarget.Execute();
            Assert.IsTrue(SabotageTargets.TryGet(SabotageKind.Battery, battery.Id, out ISabotageable batteryTarget, out _));
            batteryTarget.Execute();

            yield return Until(() => !Has(board, SabotageFactKind.Battery, battery.Id, out _)
                && Has(board, SabotageFactKind.Trap, trap.Id, out SabotageFact armed) && !armed.Available,
                5f, "the trap to read armed and the battery to disappear");
            Assert.AreEqual(before - 1, board.SabotageFacts.Count, "the trap stays listed, the battery is gone");
        }
    }
}
