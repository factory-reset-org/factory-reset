using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Tests
{
    /// <summary>
    /// Helpers that keep the prop tests in the real level timing-proof: the agents are taken out so none
    /// walks into a crate mid-test, and waits are counted in game time, so a slow editor (the full PlayMode
    /// run, or an unfocused window) only makes a test slower, never makes it fail.
    /// </summary>
    static class PropTestLevel
    {
        /// <summary>Switches every agent off for the rest of the test. The level is reloaded by the next test.</summary>
        public static void RemoveAgents()
        {
            foreach (AgentController agent in UnityEngine.Object.FindObjectsByType<AgentController>(FindObjectsInactive.Exclude))
                agent.gameObject.SetActive(false);
        }

        /// <summary>
        /// Waits until <paramref name="condition"/> holds, failing after <paramref name="gameSeconds"/> of game
        /// time. A generous real-time cap stops a stuck test from hanging the run.
        /// </summary>
        public static IEnumerator UntilGameTime(Func<bool> condition, float gameSeconds, string what, float realSecondsCap = 180f)
        {
            float gameUntil = Time.time + gameSeconds;
            float realUntil = Time.realtimeSinceStartup + realSecondsCap;
            while (!condition())
            {
                if (Time.time > gameUntil || Time.realtimeSinceStartup > realUntil)
                    Assert.Fail("Timed out waiting for " + what);
                yield return null;
            }
        }

        /// <summary>Waits until the body has stayed still for a few physics steps (or fails after the game-time limit).</summary>
        public static IEnumerator UntilAtRest(Rigidbody body, float gameSeconds, string what)
        {
            int stillSteps = 0;
            yield return UntilGameTime(() =>
            {
                stillSteps = body.linearVelocity.sqrMagnitude < 0.0025f ? stillSteps + 1 : 0;
                return stillSteps >= 5;
            }, gameSeconds, what);
        }
    }
}
