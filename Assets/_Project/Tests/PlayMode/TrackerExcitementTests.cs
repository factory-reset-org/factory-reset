using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.AI.Core;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.Animation;

namespace ToyFactory.Tests
{
    /// <summary>
    /// The Tracker's happy-puppy excitement on its real body: a brain that says "Distracted"
    /// gets the spotted hop, then the watching loop (bounce, fast wag, perked and flapping
    /// ears, head tilt, the key at 2×); leaving the state eases it all back to rest within
    /// about 0.2 s; and nothing plays while it is knocked out.
    /// </summary>
    public sealed class TrackerExcitementTests
    {
        readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
                if (created != null)
                    Object.Destroy(created);
            _created.Clear();
        }

        static WaitForEndOfFrame EndOfFrame() => new WaitForEndOfFrame();

        AgentController Tracker(StateNameBrain brain, out Transform root, out Transform tail, out Transform earLeft)
        {
            AgentBodyFixture.Floor(_created);
            AgentController agent = AgentBodyFixture.Body("TrackerToy", AgentType.Tracker, brain, _created);
            root = agent.transform.Find("TrackerToy/TrackerToy_Root");
            tail = root.Find("Body_Pivot/Tail_Pivot");
            earLeft = root.Find("Body_Pivot/Head_Pivot/Ear_L_Pivot");
            return agent;
        }

        // The tail's turn and the left ear's roll (degrees, signed) from rest (0).
        static float Yaw(Transform t) => Mathf.DeltaAngle(0f, t.localEulerAngles.y);
        static float Roll(Transform t) => Mathf.DeltaAngle(0f, t.localEulerAngles.z);

        [UnityTest]
        public IEnumerator ADistractedTrackerHopsThenBouncesWagsAndPerksUp()
        {
            var brain = new StateNameBrain();
            AgentController agent = Tracker(brain, out Transform root, out Transform tail, out Transform earLeft);
            var excitement = agent.GetComponent<TrackerExcitement>();
            var key = agent.GetComponent<WindUpKeySpinner>();
            Assert.IsNotNull(excitement, "The prefab has the excitement.");
            float restHeight = root.localPosition.y;
            yield return new WaitForSeconds(0.2f);   // settled on the floor

            brain.State = TrackerExcitement.DistractedState;
            float hop = 0f;
            bool spotted = false;
            for (float t = 0f; t < 0.4f; t += Time.deltaTime)
            {
                yield return EndOfFrame();
                spotted |= excitement.IsSpotting;
                hop = Mathf.Max(hop, root.localPosition.y - restHeight);
            }
            Assert.IsTrue(spotted, "The one shot plays when it spots the toy.");
            Assert.That(hop, Is.InRange(0.1f, 0.17f), "A hop of about 0.12 m, plus the bounce coming in.");

            float wag = 0f, earUp = 0f, bounce = 0f;
            for (float t = 0f; t < 0.6f; t += Time.deltaTime)
            {
                yield return EndOfFrame();
                wag = Mathf.Max(wag, Mathf.Abs(Yaw(tail)));
                earUp = Mathf.Max(earUp, -Roll(earLeft));   // up is −Z on the left ear
                bounce = Mathf.Max(bounce, root.localPosition.y - restHeight);
            }
            Assert.AreEqual(1f, excitement.Watching, "Stopped, so it is watching.");
            Assert.Greater(wag, 25f, "The tail wags about ±30°.");
            Assert.That(earUp, Is.InRange(20f, 31f), "Ears perked 20° and flapping ±10°.");
            Assert.That(bounce, Is.InRange(0.03f, 0.045f), "Bounces of about 0.04 m.");
            Assert.AreEqual(2f, key.SpeedMultiplier, 1e-3f, "The key winds twice as fast.");

            brain.State = "Patrol";
            yield return new WaitForSeconds(0.25f);
            yield return EndOfFrame();
            Assert.AreEqual(0f, excitement.Watching);
            Assert.AreEqual(0f, excitement.Excited);
            Assert.AreEqual(restHeight, root.localPosition.y, 1e-4f, "Back on the floor.");
            (float degrees, float metres) = agent.GetComponent<RestPoseClip>().Deviation();
            Assert.Less(degrees, 0.01f, "Tail, ears and head back at rest within 0.25 s.");
            Assert.Less(metres, 1e-4f);
            Assert.AreEqual(1f, key.SpeedMultiplier, 1e-3f);
        }

        [UnityTest]
        public IEnumerator NothingPlaysWhileItIsKnockedOut()
        {
            var brain = new StateNameBrain { State = TrackerExcitement.DistractedState };
            AgentController agent = Tracker(brain, out Transform root, out Transform tail, out _);
            var excitement = agent.GetComponent<TrackerExcitement>();
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(1f, excitement.Watching);

            agent.Disable(5f);
            yield return null;
            yield return EndOfFrame();
            Assert.AreEqual(0f, excitement.Watching, "Cut at once: the slump owns the pose.");
            Assert.AreEqual(0f, excitement.Excited);
            Assert.IsFalse(excitement.IsSpotting);
            Assert.Less(Mathf.Abs(Yaw(tail)), 0.01f, "No wag while down.");
        }
    }
}
